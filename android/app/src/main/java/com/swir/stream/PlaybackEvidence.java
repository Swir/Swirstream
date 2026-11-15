package com.swir.stream;

/** Selected renderer clocks, not pixels or the player's shared media clock. */
final class PlaybackEvidence {
    static final long CLEAR_CONFIRM_MS=500,DRM_CONFIRM_MS=12000;
    static final long UNSET=Long.MIN_VALUE;
    private final boolean drm;
    private int mask=-1;
    private long wall=UNSET,video=UNSET,audio=UNSET,sharedMs,videoMs,audioMs;
    private boolean keys;
    PlaybackEvidence(boolean drm){this.drm=drm;}
    void sample(long now,boolean active,boolean selectedVideo,boolean selectedAudio,long videoUs,long audioUs,boolean loadedKeys){
        int selected=(selectedVideo?1:0)|(selectedAudio?2:0);
        if(selected!=mask||drm&&loadedKeys!=keys){mask=selected;keys=loadedKeys;wall=now;video=videoUs;audio=audioUs;sharedMs=videoMs=audioMs=0;return;}
        long elapsed=wall==UNSET?0:now-wall,vd=step(video,videoUs),ad=step(audio,audioUs);
        wall=now;video=videoUs;audio=audioUs;
        if(!active||selected==0||elapsed<=0||elapsed>2500||drm&&!keys)return;
        if(selectedVideo&&vd>0)videoMs+=Math.min(elapsed,vd);
        if(selectedAudio&&ad>0)audioMs+=Math.min(elapsed,ad);
        if(selectedVideo&&vd<=0||selectedAudio&&ad<=0)return;
        long common=elapsed;if(selectedVideo)common=Math.min(common,vd);if(selectedAudio)common=Math.min(common,ad);sharedMs+=common;
    }
    private static long step(long before,long after){if(before==UNSET||after==UNSET||after<=before)return 0;long delta=(after-before)/1000;return delta>2500?0:delta;}
    boolean ready(){return mask>0&&(!drm||keys)&&sharedMs>=requiredMs();}
    long sharedMs(){return sharedMs;}
    long watchedMs(){return ready()?sharedMs:0;}
    long videoMs(){return videoMs;}
    long audioMs(){return audioMs;}
    long requiredMs(){return drm?DRM_CONFIRM_MS:CLEAR_CONFIRM_MS;}
}
