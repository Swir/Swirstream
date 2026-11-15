package com.swir.stream;

import java.io.IOException;

/** Android JSONTokener accepts JavaScript literals; portal auth requires JSON grammar. */
final class PortalJson {
    private final String text; private int at;
    private PortalJson(String text){this.text=text;}
    static void validate(String text)throws IOException{
        if(text==null||text.length()>32*1024*1024)throw invalid();
        PortalJson parser=new PortalJson(text);parser.value(0);parser.space();if(parser.at!=text.length())throw invalid();
    }
    private static IOException invalid(){return new IOException("Portal zwrócił nieprawidłową odpowiedź JSON.");}
    private void space(){while(at<text.length()&&" \t\r\n".indexOf(text.charAt(at))>=0)at++;}
    private boolean take(char ch){if(at<text.length()&&text.charAt(at)==ch){at++;return true;}return false;}
    private void expect(char ch)throws IOException{if(!take(ch))throw invalid();}
    private void value(int depth)throws IOException{
        if(depth>64)throw invalid();space();if(at>=text.length())throw invalid();char ch=text.charAt(at);
        if(ch=='{'){at++;space();if(take('}'))return;do{space();string();space();expect(':');value(depth+1);space();if(take('}'))return;expect(',');}while(true);}
        if(ch=='['){at++;space();if(take(']'))return;do{value(depth+1);space();if(take(']'))return;expect(',');}while(true);}
        if(ch=='"'){string();return;}
        for(String literal:new String[]{"true","false","null"})if(text.startsWith(literal,at)){at+=literal.length();return;}
        number();
    }
    private void string()throws IOException{
        expect('"');while(at<text.length()){char ch=text.charAt(at++);if(ch=='"')return;if(ch<32)throw invalid();if(ch=='\\'){if(at>=text.length())throw invalid();char escape=text.charAt(at++);if(escape=='u'){for(int i=0;i<4;i++){if(at>=text.length()||"0123456789abcdefABCDEF".indexOf(text.charAt(at++))<0)throw invalid();}}else if("\"\\/bfnrt".indexOf(escape)<0)throw invalid();}}throw invalid();
    }
    private boolean digit(){return at<text.length()&&text.charAt(at)>='0'&&text.charAt(at)<='9';}
    private void number()throws IOException{
        take('-');if(!take('0')){if(at>=text.length()||text.charAt(at)<'1'||text.charAt(at)>'9')throw invalid();while(digit())at++;}
        if(take('.')){if(!digit())throw invalid();while(digit())at++;}
        if(take('e')||take('E')){if(!take('+'))take('-');if(!digit())throw invalid();while(digit())at++;}
    }
}
