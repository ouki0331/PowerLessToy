namespace PowerLessToy.NotifyPosition.Core;

public static class IpcConstants
{
    public const string PipeName = "PowerLessToy_NotifyPosition_Pipe";
    public const string MutexName = "Global\\PowerLessToy_NotifyPosition_Host_Mutex";

    public const string CmdTest = "TEST";
    public const string CmdTest5 = "TEST 5";
    public const string CmdStart = "START";
    public const string CmdStop = "STOP";
    public const string CmdSettings = "SETTINGS";
    public const string CmdStatus = "STATUS";
    public const string CmdSetMode = "SET_MODE:";
    public const string CmdSetPosition = "SET_POS:";
    public const string CmdPing = "PING";
    public const string RespPong = "PONG";
    public const string RespOk = "OK";
}
