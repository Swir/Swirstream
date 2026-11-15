package com.swir.stream;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import androidx.media3.common.AudioAttributes;
import androidx.media3.common.C;
import androidx.media3.common.MediaItem;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.Player;
import androidx.media3.common.Tracks;
import androidx.media3.exoplayer.DefaultRenderersFactory;
import androidx.media3.exoplayer.audio.AudioSink;
import androidx.media3.exoplayer.audio.ForwardingAudioSink;
import androidx.media3.common.TrackSelectionParameters;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.datasource.DefaultDataSource;
import androidx.media3.datasource.DefaultHttpDataSource;
import androidx.media3.datasource.HttpDataSource;
import androidx.media3.exoplayer.DefaultLoadControl;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.exoplayer.drm.DefaultDrmSessionManagerProvider;
import androidx.media3.exoplayer.analytics.AnalyticsListener;
import androidx.media3.ui.PlayerView;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;

/** In-process Android playback. A ready state or HTTP response is never a playback proof. */
@UnstableApi
public final class SwirPlayer implements AutoCloseable {
    public interface Listener {
        void state(String message);
        default void playbackActive(boolean active) {}
        void confirmed(Station station,long startupMs);
        void watchedThirtySeconds(Station station);
        void failed(Station station,String message,long elapsedMs);
    }
    private final ExoPlayer player;
    private final PlayerView view;
    private final Listener listener;
    private final Station station;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final long started=SystemClock.elapsedRealtime();
    private long activeStartupMs,startupAccountedAt=started;
    private boolean startupCounting;
    private long lastProgress=started;
    private final PlaybackEvidence evidence;
    private boolean selectedVideo,selectedAudio;
    private boolean closed,confirmed,failed,learned;
    private final boolean drmRequired;
    private boolean drmKeysLoaded;
    private final AtomicLong audioPositionUs=new AtomicLong(PlaybackEvidence.UNSET);
    private final AtomicLong videoPresentationUs=new AtomicLong(Long.MIN_VALUE);
    private final Runnable tick=new Runnable(){public void run(){
        if(closed)return;
        accountStartup();
        long wall=SystemClock.elapsedRealtime(),before=evidence.sharedMs();
        boolean active=player.isPlaying();
        evidence.sample(wall,active,selectedVideo,selectedAudio,videoPresentationUs.get(),audioPositionUs.get(),drmKeysLoaded);
        if(evidence.sharedMs()>before){
            lastProgress=wall;
            if(evidence.ready())confirm();
            if(confirmed&&evidence.ready()&&!learned&&evidence.watchedMs()>=30000){learned=true;listener.watchedThirtySeconds(station);}
        }
        if(!player.getPlayWhenReady()||player.getPlaybackSuppressionReason()!=Player.PLAYBACK_SUPPRESSION_REASON_NONE)lastProgress=wall;
        if(player.getPlayWhenReady()&&player.getPlaybackSuppressionReason()==Player.PLAYBACK_SUPPRESSION_REASON_NONE&&!failed&&player.getPlaybackState()!=Player.STATE_ENDED){
            if(!confirmed&&activeStartupMs>(drmRequired?35000:15000))fail("Nie potwierdzono postępu wybranych ścieżek obrazu i dźwięku w limicie czasu.");
            else if(confirmed&&wall-lastProgress>15000&&player.getPlaybackState()!=Player.STATE_ENDED)fail("Wybrana ścieżka transmisji przestała odpowiadać.");
        }
        if(!closed)handler.postDelayed(this,500);
    }};
    public SwirPlayer(Context context,PlayerView view,Station station,String quality,Listener listener){
        this.view=view;this.station=station;this.listener=listener;this.drmRequired=!station.drmScheme.isEmpty();this.evidence=new PlaybackEvidence(drmRequired);
        Map<String,String> headers=new HashMap<>(station.requestHeaders);if(!clean(station.referrer).isEmpty())headers.put("Referer",clean(station.referrer));
        if(!clean(station.userAgent).isEmpty())headers.put("User-Agent",clean(station.userAgent));
        HttpDataSource.Factory network=station.isProvider()||!station.drmScheme.isEmpty()?new ProviderDataSource.Factory(context,station.url,headers):
            new DefaultHttpDataSource.Factory().setConnectTimeoutMs(8000).setReadTimeoutMs(12000).setAllowCrossProtocolRedirects(true)
                .setUserAgent(clean(station.userAgent).isEmpty()?"SwirStreamAndroid/0.3.0":clean(station.userAgent)).setDefaultRequestProperties(headers);
        androidx.media3.datasource.DataSource.Factory data=station.isProvider()||!station.drmScheme.isEmpty()?network:new DefaultDataSource.Factory(context.getApplicationContext(),network);
        MediaItem.Builder item=new MediaItem.Builder().setUri(station.url);
        DefaultMediaSourceFactory sources=new DefaultMediaSourceFactory(data);
        if(!station.drmScheme.isEmpty()){
            java.util.UUID uuid=drmUuid(station.drmScheme);
            if(!androidx.media3.exoplayer.drm.FrameworkMediaDrm.isCryptoSchemeSupported(uuid))throw new IllegalArgumentException("Ten system DRM nie jest obsługiwany przez urządzenie.");
            item.setDrmConfiguration(new MediaItem.DrmConfiguration.Builder(uuid).setLicenseUri(station.drmLicenseUrl).setForceDefaultLicenseUri(true).setLicenseRequestHeaders(station.drmHeaders).setMultiSession(true).build());
            DefaultDrmSessionManagerProvider drm=new DefaultDrmSessionManagerProvider();
            drm.setDrmHttpDataSourceFactory(new ProviderDataSource.Factory(context,station.drmLicenseUrl,station.drmHeaders));sources.setDrmSessionManagerProvider(drm);
        }
        DefaultRenderersFactory renderers=new DefaultRenderersFactory(context.getApplicationContext()){
            @Override protected AudioSink buildAudioSink(Context owner,boolean floating,boolean parameters){
                AudioSink sink=super.buildAudioSink(owner,floating,parameters);if(sink==null)return null;
                return new ForwardingAudioSink(sink){
                    @Override public long getCurrentPositionUs(boolean ended){long value=super.getCurrentPositionUs(ended);audioPositionUs.set(value==AudioSink.CURRENT_POSITION_NOT_SET?PlaybackEvidence.UNSET:value);return value;}
                    @Override public void flush(){audioPositionUs.set(PlaybackEvidence.UNSET);super.flush();}
                    @Override public void reset(){audioPositionUs.set(PlaybackEvidence.UNSET);super.reset();}
                };
            }
        };
        player=new ExoPlayer.Builder(context.getApplicationContext(),renderers).setMediaSourceFactory(sources)
                .setLoadControl(new DefaultLoadControl.Builder().setBufferDurationsMs(1800,15000,700,1500).build()).build();
        player.setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(),true);
        player.setVideoFrameMetadataListener((presentationTimeUs,releaseTimeNs,format,mediaFormat)->videoPresentationUs.set(presentationTimeUs));
        player.setHandleAudioBecomingNoisy(true);setQuality(quality);view.setPlayer(player);view.setUseController(true);view.setControllerShowTimeoutMs(3500);
        player.addAnalyticsListener(new AnalyticsListener(){
            @Override public void onDrmKeysLoaded(AnalyticsListener.EventTime eventTime,androidx.media3.exoplayer.drm.KeyRequestInfo keyRequestInfo){
                if(closed||failed)return;drmKeysLoaded=true;
            }
        });
        player.addListener(new Player.Listener(){
            @Override public void onPlayWhenReadyChanged(boolean ready,int reason){accountStartup();}
            @Override public void onPlaybackSuppressionReasonChanged(int reason){accountStartup();}
            @Override public void onTracksChanged(Tracks tracks){selectedVideo=tracks.isTypeSelected(C.TRACK_TYPE_VIDEO);selectedAudio=tracks.isTypeSelected(C.TRACK_TYPE_AUDIO);}
            @Override public void onPlayerError(PlaybackException error){fail("Źródło niedostępne: "+error.getErrorCodeName());}
            @Override public void onPlaybackStateChanged(int state){if(closed||failed)return;
                if(state==Player.STATE_BUFFERING)listener.state(confirmed?"Uzupełnianie bufora…":"Łączenie ze źródłem…");
                else if(state==Player.STATE_READY)listener.state(confirmed?"Odtwarzanie w Swir Player":"Sprawdzanie obrazu i dźwięku…");
                else if(state==Player.STATE_ENDED)listener.state("Transmisja zakończona");
            }
            @Override public void onIsPlayingChanged(boolean playing){if(closed)return;listener.playbackActive(playing&&!failed);if(confirmed&&!failed)listener.state(playing?"Odtwarzanie w Swir Player":"Wstrzymano odtwarzanie");}
        });
        player.setMediaItem(item.build());player.prepare();player.play();handler.postDelayed(tick,500);
    }
    static java.util.UUID drmUuid(String scheme){if("widevine".equalsIgnoreCase(scheme))return C.WIDEVINE_UUID;if("clearkey".equalsIgnoreCase(scheme))return C.CLEARKEY_UUID;throw new IllegalArgumentException("Obsługiwane systemy: Widevine i ClearKey.");}
    public static String drmCapabilities(){return "DRM urządzenia: Widevine "+drmStatus(C.WIDEVINE_UUID)+" • ClearKey "+drmStatus(C.CLEARKEY_UUID)+". Odtwarzanie zależy również od licencji operatora i formatu transmisji.";}
    private static String drmStatus(java.util.UUID uuid){try{return androidx.media3.exoplayer.drm.FrameworkMediaDrm.isCryptoSchemeSupported(uuid)?"dostępny":"brak";}catch(Exception error){return "niedostępny";}}
    private static String clean(String value){return value==null||value.contains("\r")||value.contains("\n")?"":value;}
    private void accountStartup(){long now=SystemClock.elapsedRealtime();if(startupCounting&&!confirmed&&!failed)activeStartupMs+=Math.max(0,now-startupAccountedAt);startupAccountedAt=now;startupCounting=!confirmed&&!failed&&player.getPlayWhenReady()&&player.getPlaybackSuppressionReason()==Player.PLAYBACK_SUPPRESSION_REASON_NONE;}
    private void confirm(){if(closed||failed||confirmed||!evidence.ready())return;accountStartup();confirmed=true;startupCounting=false;lastProgress=SystemClock.elapsedRealtime();listener.confirmed(station,activeStartupMs);}
    private void fail(String message){if(closed||failed)return;accountStartup();failed=true;startupCounting=false;player.stop();listener.failed(station,message,activeStartupMs);}
    public void setQuality(String quality){
        TrackSelectionParameters.Builder options=player.getTrackSelectionParameters().buildUpon().setForceHighestSupportedBitrate(false).setForceLowestBitrate(false);
        if("Najwyższa jakość".equals(quality))options.setMaxVideoSize(Integer.MAX_VALUE,Integer.MAX_VALUE).setMaxVideoBitrate(Integer.MAX_VALUE).setForceHighestSupportedBitrate(true);
        else if("Oszczędzaj transfer".equals(quality))options.setMaxVideoSize(640,360).setMaxVideoBitrate(900000).setForceLowestBitrate(true);
        else options.setMaxVideoSize(1280,720).setMaxVideoBitrate(3500000);
        player.setTrackSelectionParameters(options.build());
    }
    public void pause(){if(!closed)player.pause();}
    public void toggle(){if(!closed){if(player.getPlayWhenReady())player.pause();else player.play();}}
    public void mute(){if(!closed)player.setVolume(player.getVolume()>0?0:1);}
    public boolean isConfirmed(){return confirmed;}
    public boolean hasDrmKeys(){return drmKeysLoaded;}
    public boolean isPlaying(){return !closed&&player.isPlaying();}
    public boolean wantsPlayback(){return !closed&&player.getPlayWhenReady();}
    public boolean isClosed(){return closed;}
    public long watchedMillis(){return evidence.watchedMs();}
    public long verifiedVideoMillis(){return evidence.videoMs();}
    public long verifiedAudioMillis(){return evidence.audioMs();}
    public long confirmationWindowMillis(){return evidence.requiredMs();}
    public boolean selectedVideo(){return selectedVideo;}
    public boolean selectedAudio(){return selectedAudio;}
    public boolean isFailed(){return failed;}
    public long position(){return closed?0:player.getCurrentPosition();}
    @Override public void close(){if(closed)return;closed=true;listener.playbackActive(false);handler.removeCallbacksAndMessages(null);view.setPlayer(null);player.release();}
}
