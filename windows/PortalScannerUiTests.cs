namespace SwirStreamAI;
public sealed partial class MainForm
{
    public Task PortalScannerUiTest(Action<string,bool,object?> check,string screenshot)
    {
        check("Release UI keeps bulk portal probing off", !Controls.Find("portal-scanner-open",true).Any(), null);
        return Task.CompletedTask;
    }
}
