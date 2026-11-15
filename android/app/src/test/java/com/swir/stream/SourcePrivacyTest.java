package com.swir.stream;
import org.junit.Test;
import static org.junit.Assert.*;
public class SourcePrivacyTest {
    @Test public void credentialsAreRoutedToVault(){for(String input:new String[]{"https://host.example/get.php?username=me&password=secret","https://host.example/live/me/secret/42.ts","https://me:secret@host.example/live","https://host.example/epg.xml?token=secret","https://host.example/a.m3u8|Authorization=Bearer%20secret"})assertTrue(input,SourcePrivacy.requiresVault(input));}
    @Test public void ordinaryPublicChannelsAndNamesStillImport(){assertFalse(SourcePrivacy.requiresVault("#EXTM3U\n#EXTINF:-1,Password TV\nhttps://host.example/live/master.m3u8\n"));assertFalse(SourcePrivacy.requiresVault("https://host.example/epg.xml?country=pl"));assertFalse(SourcePrivacy.requiresVault(""));}
}
