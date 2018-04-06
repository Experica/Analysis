using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using VLab;
using VLabAnalysis;
using System.Threading;
using MathNet.Numerics.Random;

public class SignalTests
{
    ISignal signal = new RippleSignal(digitalIPI: 20, analogIPI: 80);
    Thread[] threads;
    int maxsleep = 50; // ms
    int maxapicall = 900000000;
    MersenneTwister rng = new MersenneTwister(true);

    List<double>[] spike;
    List<int>[] uid;
    List<double[,]> lfp;
    List<double> lfpstarttime;
    List<double>[] dintime;
    List<int>[] dinvalue;

    [Test]
    public void MultiThreadRandomCall()
    {
        threads = new[] { new Thread(threadrandomcall), new Thread(threadrandomcall), new Thread(threadrandomcall) };
        Assert.True(signal.IsOnline);
        foreach (var t in threads)
        {
            t.Start();
        }
        foreach (var t in threads)
        {
            t.Join();
        }
    }

    void threadrandomcall()
    {
        for (var i = 0; i < maxapicall; i++)
        {
            Thread.Sleep(rng.Next(maxsleep));
            switch (rng.Next(5))
            {
                case 1:
                    Assert.True(signal.Start(true));
                    break;
                case 2:
                    Assert.True(signal.Stop(true));
                    break;
                case 3:
                    Assert.True(signal.Restart(true));
                    break;
                case 4:
                    Assert.True(signal.Read(out spike, out uid, out lfp, out lfpstarttime, out dintime, out dinvalue));
                    break;
                default:
                    Assert.Positive(signal.Time);
                    break;
            }
        }
    }

    // A UnityTest behaves like a coroutine in PlayMode
    // and allows you to yield null to skip a frame in EditMode
    [UnityTest]
    public IEnumerator NewTestScriptWithEnumeratorPasses()
    {
        // Use the Assert class to test conditions.
        // yield to skip a frame
        yield return null;
    }
}
