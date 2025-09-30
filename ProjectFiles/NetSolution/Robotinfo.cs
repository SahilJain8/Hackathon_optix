#region Using directives
using System;
using System.Threading;
using System.Threading.Tasks;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.EventLogger;
using FTOptix.Recipe;
#endregion

public class Robotinfo : BaseNetLogic
{
    private CancellationTokenSource _cts;
    private Task _worker;
    private readonly Random _rng = new Random();

    private IUAVariable airT, procT, torque, rpm, velocity, toolWear, load, productId, temp, isConnected, isfaulty, Running, isBatterylow, disconnected, busy;

    public override void Start()
    {
        // variables must be siblings of this NetLogic node (same folder)
        airT = LogicObject.GetVariable("airtemperature");
        procT = LogicObject.GetVariable("processtemperature");
        torque = LogicObject.GetVariable("torque");
        rpm = LogicObject.GetVariable("rotationalspeed");
        velocity = LogicObject.GetVariable("velocity");
        toolWear = LogicObject.GetVariable("toolwear");
        load = LogicObject.GetVariable("Load");
        productId = LogicObject.GetVariable("ProductType");   // "L" | "M" | "H"
        temp = LogicObject.GetVariable("Temperature");
        isConnected = LogicObject.GetVariable("isConnected");
        disconnected = LogicObject.GetVariable("Disconnected");
        busy = LogicObject.GetVariable("Busy");
        isBatterylow = LogicObject.GetVariable("isBatteryLOw");
        isfaulty = LogicObject.GetVariable("Faulty");
        Running = LogicObject.GetVariable("Running");
        // quick null checks
        //if (airT == null || procT == null || torque == null || rpm == null || velocity == null || toolWear == null || load == null || productId == null)
        //throw new InvalidOperationException("One or more simulation variables not found next to the NetLogic.");

        // seeds (UAValue required)
        airT.Value = new UAValue(300f);
        procT.Value = new UAValue(310f);
        torque.Value = new UAValue(40f);
        rpm.Value = new UAValue(1000f);
        velocity.Value = new UAValue(0.4f);
        toolWear.Value = new UAValue(0f);
        load.Value = new UAValue(30f);
        if (string.IsNullOrEmpty(Convert.ToString(((UAValue)productId.Value).Value)))
            productId.Value = new UAValue("M");

        // local state (so we don't read UA each tick)
        float air = 300f;
        float tq = 40f;
        float ld = 30f;

        _cts = new CancellationTokenSource();
        var tok = _cts.Token;

        bool faulty = _rng.Next(0, 100) < 10;  // e.g. 10% chance faulty
        isfaulty.Value = new UAValue(faulty);
        faulty = true;
        if (faulty)
        {
            isConnected.Value = new UAValue(false);
            Running.Value = new UAValue(false);
            isBatterylow.Value = new UAValue(false);
        }
        else
        {
            _worker = Task.Run(async () =>
            {
                const float P = 2860f;                 // W
                const float rpmMin = 500f, rpmMax = 1800f;

                while (!tok.IsCancellationRequested)
                {
                    // ---------- existing logic ----------
                    air += Jitter(0.2f);
                    if (air < 296f) air = 296f;
                    if (air > 304f) air = 304f;
                    airT.Value = new UAValue(air);

                    procT.Value = new UAValue(air + 10f + Jitter(0.3f));
                    temp.Value = procT.Value;

                    tq = 40f + Jitter(6f);
                    if (tq < 5f) tq = 5f;
                    torque.Value = new UAValue(tq);

                    float r = (P / tq) * (60f / (2f * MathF.PI)) + Jitter(20f);
                    if (r < rpmMin) r = rpmMin;
                    if (r > rpmMax) r = rpmMax;
                    rpm.Value = new UAValue(r);

                    float v = (r - rpmMin) / (rpmMax - rpmMin);
                    if (v < 0f) v = 0f; if (v > 1f) v = 1f;
                    velocity.Value = new UAValue(v);

                    ld = 20f + tq * 0.6f + Jitter(2f);
                    if (ld < 0f) ld = 0f; if (ld > 100f) ld = 100f;
                    load.Value = new UAValue(ld);

                    string pt = Convert.ToString(((UAValue)productId.Value).Value);
                    float inc = (pt == "H") ? 5f : (pt == "M") ? 3f : 2f;
                    float twNow = Convert.ToSingle(((UAValue)toolWear.Value).Value);
                    toolWear.Value = new UAValue(twNow + inc);

                // ---------- new status logic ----------



                
                        isConnected.Value = new UAValue(true);
                        Running.Value = new UAValue(true);

                        bool batteryLow = _rng.Next(0, 100) < 30; // 30% chance low battery
                        isBatterylow.Value = new UAValue(batteryLow);
                    

                    disconnected.Value = !isConnected.Value;
                    busy.Value = !Running.Value;


                    await Task.Delay(500, tok); // 2 Hz
                }
            }, tok);

        }
      



    }

    public override void Stop()
    {
        try { _cts?.Cancel(); _worker?.Wait(250); }
        catch { }
        finally { _cts?.Dispose(); _cts = null; _worker = null; }
    }

    private float Jitter(float sigma) => (float)((_rng.NextDouble() * 2 - 1) * sigma);
}
