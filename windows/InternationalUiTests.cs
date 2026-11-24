using System.Globalization;
using System.Text.Json;

namespace SwirStreamAI;

internal static class InternationalUiTests
{
    public static int Run(string parent,IntentNet model,string report)
    {
        string data=Path.Combine(parent,"international-"+Guid.NewGuid().ToString("N"));
        List<string> checks=[];
        void Check(string name,bool ok){if(!ok)throw new InvalidDataException(name);checks.Add(name);}
        IEnumerable<Control> Descend(Control tree) => tree.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descend(c)));
        try
        {
            UiLanguage.Load(data);UiLanguage.Save("en");UiLanguage.Load(data);
            using(var splash=new StartupWindow()) Check("English first scan",Descend(splash).Any(c=>c.Text=="Searching for channels…"));
            using(var form=new MainForm(data,model,autoDiscover:false)) {
                var controls=Descend(form).ToList();
                Check("Main player opens with English search and playback controls",new[]{"Search the web","Full screen","Deep Search","Language / Język"}.All(label=>controls.Any(c=>c.Text==label)));
            }
            Check("British names and code share a filter",Countries.Load().Single(c=>c.Code=="GB") is var uk && uk.Matches("Brytania") && uk.Matches("United Kingdom") && uk.Matches("gb") && uk.ToString().StartsWith("United Kingdom"));
            Check("Device region and global fallback",UiLanguage.InitialCountries("JP").SequenceEqual(new[]{"JP"}) && UiLanguage.InitialCountries("ZZ").Length==0);
            Check("Native country names work across interface languages",Countries.Load().Single(c=>c.Code=="JP").Matches("日本") && Countries.Load().Single(c=>c.Code=="DE").Matches("Deutschland"));
            var store=new LibraryStore(data);store.Data.SelectedCountries=["BR","JP"];store.Save();
            UiLanguage.Save("pl");UiLanguage.Load(data);
            using(var splash=new StartupWindow()) Check("Polish selection persists",Descend(splash).Any(c=>c.Text=="Proszę czekać — trwa wyszukiwanie kanałów"));
            Check("Saved countries survive language change",new LibraryStore(data).Data.SelectedCountries.SequenceEqual(new[]{"BR","JP"}));
            foreach(string name in new[]{"Télé Québec","テレビ東京","ČT24","Rádio São Paulo","Česká TV"}) Check("Public search retains "+name,ChannelSearch.WebQuery(name,[]).Query==name);
            Check("Fullwidth channel names and numeric boundaries", ChannelSearch.Exact("NHK1",[new("ＮＨＫ１","")]).Count==1 && !ChannelSearch.Matches("NHK1",new("ＮＨＫ１０","")));
            File.WriteAllText(report,JsonSerializer.Serialize(new{success=true,checks})); return 0;
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{success=false,checks,error=ex.ToString()}));return 1;}
    }
}
