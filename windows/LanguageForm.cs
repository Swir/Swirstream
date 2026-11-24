using static SwirStreamAI.UiLanguage;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    Task SelectLanguage()
    {
        using var dialog = new Form { Text = T("Wybór języka"), ClientSize=new(400,170),
            StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog,
            BackColor=Card,ForeColor=Color.White,Font=Font,MinimizeBox=false,MaximizeBox=false };
        var panel = new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new(16),FlowDirection=FlowDirection.TopDown,WrapContents=false };
        string[] keys=["system","pl","en"];
        var picker=new ComboBox { Width=350,DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName=T("Wybór języka") };
        picker.Items.AddRange([T("Język systemowy"),"Polski","English"]);
        picker.SelectedIndex=Array.IndexOf(keys,Preference);
        panel.Controls.Add(picker);
        panel.Controls.Add(new Label { Text=T("Język zmieni się przy następnym uruchomieniu programu."),Width=350,Height=45 });
        var actions=new FlowLayoutPanel{AutoSize=true};
        var save=new Button{Text=T("Zapisz"),DialogResult=DialogResult.OK,AutoSize=true};
        var cancel=new Button{Text=T("Anuluj"),DialogResult=DialogResult.Cancel,AutoSize=true};
        actions.Controls.AddRange([save,cancel]);panel.Controls.Add(actions);dialog.Controls.Add(panel);
        dialog.AcceptButton=save;dialog.CancelButton=cancel;
        if(dialog.ShowDialog(this)==DialogResult.OK) { UiLanguage.Save(keys[picker.SelectedIndex]);status.Text=T("Zapisano język interfejsu."); }
        return Task.CompletedTask;
    }
}
