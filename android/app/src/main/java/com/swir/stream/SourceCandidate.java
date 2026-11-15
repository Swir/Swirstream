package com.swir.stream;

import org.json.JSONObject;
import java.io.IOException;

/** Public metadata only. A discovered link is never a successful playback record. */
final class SourceCandidate {
    final String id,url,title,evidenceUrl,status;final SourceExplorerRules.Kind kind;
    final long discoveredAt,checkedAt,modifiedAt;final int channelCount;final boolean protectedMedia;
    SourceCandidate(String url,String title,String evidence,SourceExplorerRules.Kind kind,String status,long discovered,long checked,long modified,int channels,boolean protectedMedia)throws IOException{
        long now=System.currentTimeMillis();if(discovered<=0||discovered>now+60000||checked<0||checked>now+60000||modified<0||modified>now+600000||channels<0||channels>100000||kind==null)throw new IOException("Nieprawidłowy zapis wyniku eksploratora.");this.url=SourceExplorerHttp.allowed(url);this.evidenceUrl=SourceExplorerHttp.allowed(evidence);this.id=Station.sha256(this.url);this.title=safeText(title,"Źródło");this.status=safeText(status,"Nie potwierdzono transmisji");this.kind=kind;this.discoveredAt=discovered;this.checkedAt=checked;this.modifiedAt=modified;this.channelCount=channels;this.protectedMedia=protectedMedia;
    }
    private static String safeText(String input,String fallback){if(input==null||input.isEmpty()||SourceExplorerRules.privateText(input)||input.contains("://"))return fallback;return input.replaceAll("[\\p{Cntrl}]"," ").substring(0,Math.min(240,input.length()));}
    JSONObject json()throws Exception{return new JSONObject().put("id",id).put("url",url).put("title",title).put("evidenceUrl",evidenceUrl).put("kind",kind.name()).put("status",status).put("discoveredAt",discoveredAt).put("checkedAt",checkedAt).put("modifiedAt",modifiedAt).put("channelCount",channelCount).put("protectedMedia",protectedMedia);}
    static SourceCandidate read(JSONObject value)throws Exception{SourceCandidate result=new SourceCandidate(value.getString("url"),value.getString("title"),value.getString("evidenceUrl"),SourceExplorerRules.Kind.valueOf(value.getString("kind")),value.getString("status"),value.getLong("discoveredAt"),value.getLong("checkedAt"),value.getLong("modifiedAt"),value.getInt("channelCount"),value.getBoolean("protectedMedia"));if(!result.id.equals(value.getString("id")))throw new IOException("Niezgodna tożsamość wyniku.");return result;}
}
