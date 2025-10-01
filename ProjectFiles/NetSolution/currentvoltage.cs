#region Using directives
using System;
using System.Threading;
using System.Threading.Tasks;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.Core;
using FTOptix.EventLogger;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.Store;
using FTOptix.DataLogger;
#endregion

public class currentvoltage : BaseNetLogic
{
    private CancellationTokenSource _cts;
    private Task _worker;

    private IUAVariable _currentVar;
    private IUAVariable _voltageVar, faulty;

    // ---------- Tunables ----------
    const float CurrentBaseline = 12.5f;   // amps
    const float VoltageBaseline = 48.0f;   // volts

    const float SmallNoiseAmpI = 0.08f;    // small random jiggle
    const float SmallNoiseAmpV = 0.05f;

    const float DipDepthI = 4.0f;          // how far a dip pulls down (amps)
    const float DipDepthV = 6.0f;          // how far a dip pulls down (volts)

    const int DipDurationMs = 1200;      // time spent near the bottom of the dip
    const int RecoverMs = 1800;      // smooth recovery time
    const int IntervalMs = 100;       // update rate ~10Hz

    const double DipChancePerTick = 0.02;  // ≈2% each tick ⇒ ~one dip every ~5s on average
    // --------------------------------

    public override void Start()
    {
        _currentVar = LogicObject.GetVariable("current");
        _voltageVar = LogicObject.GetVariable("voltage");
        faulty = LogicObject.GetVariable("faulty");

        if (_currentVar == null) throw new InvalidOperationException("Variable 'current' not found under this LogicObject.");
        if (_voltageVar == null) throw new InvalidOperationException("Variable 'voltage' not found under this LogicObject.");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        if (faulty.Value)
        {
            return;
        }
        _worker = Task.Run(async () =>
        {
            var rng = new Random();

            // start at baseline
            float current = CurrentBaseline;
            float voltage = VoltageBaseline;

            bool dipping = false;
            int dipPhaseMs = 0;     // elapsed time inside dip (ms)
            int dipTotalMs = 0;     // total ms across dip+recover
            float startI = current, startV = voltage;

            while (!token.IsCancellationRequested)
            {
                // Start a dip randomly (keep it occasional)
                if (!dipping && rng.NextDouble() < DipChancePerTick)
                {
                    dipping = true;
                    dipPhaseMs = 0;
                    dipTotalMs = DipDurationMs + RecoverMs;
                    startI = current;
                    startV = voltage;
                }

                if (dipping)
                {
                    dipPhaseMs += IntervalMs;
                    if (dipPhaseMs <= DipDurationMs)
                    {
                        // Go down toward the bottom (ease-in)
                        float t = (float)dipPhaseMs / DipDurationMs;
                        float ease = EaseInOutQuad(t);
                        current = Lerp(startI, CurrentBaseline - DipDepthI, ease);
                        voltage = Lerp(startV, VoltageBaseline - DipDepthV, ease);
                    }
                    else if (dipPhaseMs <= dipTotalMs)
                    {
                        // Recover back to baseline (ease-out)
                        float t = (float)(dipPhaseMs - DipDurationMs) / RecoverMs;
                        float ease = EaseInOutQuad(t);
                        current = Lerp(CurrentBaseline - DipDepthI, CurrentBaseline, ease);
                        voltage = Lerp(VoltageBaseline - DipDepthV, VoltageBaseline, ease);
                    }
                    else
                    {
                        dipping = false;
                        current = CurrentBaseline;
                        voltage = VoltageBaseline;
                    }
                }
                else
                {
                    // Stable with tiny jitter (90% of the time)
                    current = CurrentBaseline + Jitter(rng, SmallNoiseAmpI);
                    voltage = VoltageBaseline + Jitter(rng, SmallNoiseAmpV);
                }

                // Write to tags
                _currentVar.Value = current;
                _voltageVar.Value = voltage;

                await Task.Delay(IntervalMs, token).ConfigureAwait(false);
            }
        }, token);
    }

    public override void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _worker?.Wait(500); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _worker = null;
    }

    // ----- helpers -----
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static float EaseInOutQuad(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
    }

    private static float Jitter(Random rng, float amp)
    {
        // small symmetric noise in [-amp, +amp]
        return (float)((rng.NextDouble() * 2.0 - 1.0) * amp);
    }
}
