package com.swir.stream;

import org.json.JSONObject;
import org.junit.Test;
import static org.junit.Assert.*;

/** Strict JSON type tests use no operator data or network. */
public final class PortalProfileTest {
    private boolean active(String json)throws Exception{return ProviderAdapters.portalProfileActive(new JSONObject(json));}
    @Test public void requiresPositiveIntegralId()throws Exception{
        PortalJson.validate(" {\"js\":{\"id\":12,\"status\":false,\"nested\":[null,true,-1,1.2e-3,\"escaped\\n\"]}} \r\n");
        for(String raw:new String[]{"{\"id\":0x10}","{\"id\":+1}","{\"id\":00}","{\"id\":1} trailing","{\"id\":1,\"blocked\":FALSE}","{\"id\":1,\"blocked\":NULL}","{\"id\":1,}","[1,]","{id:1}","/* comment */{}","{\"id\":.1}"}){try{PortalJson.validate(raw);fail("Non-JSON literal accepted");}catch(java.io.IOException expected){}}
        assertTrue(active("{\"id\":1}"));assertTrue(active("{\"id\":9223372036854775807}"));assertTrue(active("{\"id\":\" 00123 \"}"));
        for(String value:new String[]{"null","true","false","0","-1","1.0","1e0","1e-999","9223372036854775808","\"9223372036854775808\"","\"1.5\"","\"+1\"","\"\"","[]","{}"})assertFalse(value,active("{\"id\":"+value+"}"));assertFalse(active("{}"));assertFalse(ProviderAdapters.portalProfileActive(null));
    }
    @Test public void acceptsOnlyKnownClearFlagForms()throws Exception{
        for(String value:new String[]{"null","false","0","\"0\"","\"false\"","\" FALSE \"","\"  \""})assertTrue(value,active("{\"id\":12,\"status\":"+value+",\"blocked\":"+value+"}"));
        assertTrue(ProviderAdapters.portalProfileActive(new JSONObject().put("id"," \t12\r\n").put("status","\tfalse\n")));
        for(char ch:new char[]{0,31,160,0x2003}){assertFalse(ProviderAdapters.portalProfileActive(new JSONObject().put("id",ch+"12"+ch)));for(String field:new String[]{"status","blocked"})for(String value:new String[]{"", "0", "false"})assertFalse(ProviderAdapters.portalProfileActive(new JSONObject().put("id",12).put(field,ch+value+ch)));}
    }
    @Test public void rejectsBlockedBooleanAndNumericValues()throws Exception{
        for(String field:new String[]{"status","blocked"})for(String value:new String[]{"true","1","-1","\"true\"","\"1\"","\"-1\""})assertFalse(field+value,active("{\"id\":12,\""+field+"\":"+value+"}"));
    }
    @Test public void rejectsUnknownNonemptyAndStructuredFlags()throws Exception{
        assertFalse(ProviderAdapters.portalProfileActive(new JSONObject().put("id",12).put("blocked","fal\u017fe")));
        for(String field:new String[]{"status","blocked"})for(String value:new String[]{"\"active\"","\"blocked\"","\"unknown\"","\"00\"","\"0.0\"","\"false extra\"","[]","{}"})assertFalse(field+value,active("{\"id\":12,\""+field+"\":"+value+"}"));
    }
    @Test public void decimalAndUnderflowCannotCoerceToClear()throws Exception{
        for(String field:new String[]{"status","blocked"})for(String value:new String[]{"0.0","0e0","1e-999","0.1","-0.1"})assertFalse(field+value,active("{\"id\":12,\""+field+"\":"+value+"}"));
    }
    @Test public void clientNormalizationPreservesEncodedProviderPrefix()throws Exception{
        assertEquals("https://fixture.example:8443/panel%20one/%2Fkeep/stalker_portal/server/load.php",ProviderAdapters.portalEndpoint("https://fixture.example:8443/panel%20one/%2Fkeep/stalker_portal/c/index.html"));
        assertEquals("https://fixture.example/panel%20one/portal.php",ProviderAdapters.portalEndpoint("https://fixture.example/panel%20one/portal.php"));
        assertEquals("https://fixture.example/panel%2520one/server/load.php",ProviderAdapters.portalEndpoint("https://fixture.example/panel%2520one/c/"));
    }
}
