#region Using directives
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NativeUI;
using FTOptix.NetLogic;
using FTOptix.Retentivity;
using FTOptix.UI;
using System;
using System.Threading;
using System.Threading.Tasks;
using UAManagedCore;
using FTOptix.ODBCStore;
using FTOptix.Store;
using FTOptix.DataLogger;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;
using FTOptix.Recipe;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class SpeedAccelaration : BaseNetLogic
{
    private CancellationTokenSource _cts;
    private Task _worker;
    private IUAVariable _velocity;
    private IUAVariable _acc;
    private IUAVariable _isActive;   // <-- keep as a variable
    private bool _led;
    // <-- field that mirrors _isActive.Value
    private IUAVariable temp;
    private IUAVariable log1;
    private IUAVariable log3;
    private IUAVariable log2;
    private IUAVariable log4;

    public override void Start()
    {
        _velocity = LogicObject.GetVariable("velocity");
        _acc = LogicObject.GetVariable("acc");
        _isActive = LogicObject.GetVariable("PowerON");
        temp = LogicObject.GetVariable("Variable2");

        if (_velocity == null) throw new InvalidOperationException("Variable 'velocity' not found.");
        if (_acc == null) throw new InvalidOperationException("Variable 'acc' not found.");
        if (_isActive == null) throw new InvalidOperationException("Variable 'isactive' not found.");

      
        _led = (Boolean)_isActive.Value;

        if (_led) StartRandomizer();


    }

    public override void Stop()
    {
        StopRandomizer();
    }

    [ExportMethod]
    public void StopRandomizerAndZero()
    {
        StopRandomizer();
        _velocity.Value = 0f;
        _acc.Value = 0f;
    }

    [ExportMethod]
    public void StartRandomizer()
    {
        // Always re-read the LED state at call time
        _isActive = LogicObject.GetVariable("PowerON");
        _led = (Boolean)_isActive.Value;
        Log.Info($"LED active: {_led}");

        if (!_led) StopRandomizerAndZero();

        if (_cts != null)
            StopRandomizer();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var rand = new Random();
        const float min = 10f, max = 100f;

        _worker = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                _velocity.Value = rand.NextSingle() * (max - min) + min;
                _acc.Value = rand.NextSingle() * (max - min) + min;
                await Task.Delay(300, token).ConfigureAwait(false);
            }
        }, token);
    }

    private void StopRandomizer()
    {
        var cts = _cts;
        if (cts == null) return;

        try
        {
            cts.Cancel();
            _worker?.Wait();
        }
        catch (AggregateException) { }
        finally
        {
            cts.Dispose();
            _cts = null;
            _worker = null;
        }
    }

    [ExportMethod]
    public void Logs()
    {
       
        

        log1 = LogicObject.GetVariable("log1");
        log3 = LogicObject.GetVariable("log3");
        log2 = LogicObject.GetVariable("log2");
        log4 = LogicObject.GetVariable("log4");


        log4.Value = log3.Value;
        log3.Value = log2.Value;
        log2.Value = "log at" + " " + DateTime.Now.ToString() +" " + "is" + " " +  log1.Value; 



        // Insert code to be executed by the method
    }
}


