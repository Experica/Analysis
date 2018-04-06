/*
VLADataSet.cs is part of the VLAB project.
Copyright (c) 2016 Li Alex Zhang and Contributors

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included 
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using VLab;
using System;
using System.Linq;
using System.IO;
using Ripple;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using MathNet.Numerics.Statistics;
using System.Data;

namespace VLabAnalysis
{
    /// <summary>
    /// Thread safe data container for an analysis session.
    /// </summary>
    public class VLADataSet
    {
        Experiment ex;
        List<double>[] spike;
        List<int>[] uid;
        List<double[,]> lfp;
        List<double> lfpstarttime;
        List<double>[] dintime;
        List<int>[] dinvalue;

        List<int> condindex, condrepeat;
        List<List<Dictionary<string, double>>> condstate;
        List<double> condontime, condofftime;
        Dictionary<string, List<object>> condtestcond;

        int nrecentct; double vlabt0; double datalatency; bool isdinmark; bool isdinmarkerror;
        readonly int condch, markch, btch, etch, nmpc, msr, oll;
        readonly object apilock = new object();

        public VLADataSet(int condchannel = 1, int markchannel = 2, int begintriggerchannel = 3, int endtriggerchannel = 4,
            int nmarkpercond = 2, int marksearchradius = 20, int onlinelatency = 20)
        {
            condch = condchannel;
            markch = markchannel;
            btch = begintriggerchannel;
            etch = endtriggerchannel;
            nmpc = nmarkpercond;
            msr = marksearchradius;
            oll = onlinelatency;
            Reset();
        }

        public void Reset()
        {
            lock (apilock)
            {
                ex = null;
                spike = null;
                uid = null;
                lfp = null;
                lfpstarttime = null;
                dintime = null;
                dinvalue = null;

                condindex = null;
                condrepeat = null;
                condstate = null;
                condontime = new List<double>();
                condofftime = new List<double>();
                condtestcond = new Dictionary<string, List<object>>();

                nrecentct = 0;
                vlabt0 = 0;
                datalatency = msr + oll;
                isdinmark = false;
                isdinmarkerror = false;
            }
            GC.Collect();
        }

        public Experiment Ex
        {
            get { lock (apilock) { return ex; } }
            set
            {
                lock (apilock)
                {
                    ex = value;
                    if (ex != null)
                    {
                        datalatency = ex.Latency + msr + ex.Delay + oll;
                    }
                }
            }
        }

        public List<double>[] Spike { get { lock (apilock) { return spike; } } }

        public List<int>[] UID { get { lock (apilock) { return uid; } } }

        public List<int> CondIndex { get { lock (apilock) { return condindex; } } }

        public List<int> CondRepeat { get { lock (apilock) { return condrepeat; } } }

        public List<double> CondOnTime { get { lock (apilock) { return condontime; } } }

        public List<double> CondOffTime { get { lock (apilock) { return condofftime; } } }

        public Dictionary<string, List<object>> CondTestCond { get { lock (apilock) { return condtestcond; } } }

        public double VLabTimeZero { get { lock (apilock) { return vlabt0; } } }

        public double VLabTimeToDataTime(double vlabtime)
        {
            lock (apilock)
            {
                return vlabtime * (1 + (ex == null ? 0 : ex.TimerDriftSpeed)) + vlabt0;
            }
        }

        public double DataLatency { get { lock (apilock) { return datalatency; } } }

        public void Remove(double time)
        {
            lock (apilock)
            {
                if (spike != null)
                {
                    for (var i = 0; i < spike.Length; i++)
                    {
                        if (spike[i].Count > 0)
                        {
                            VLAExtention.Sub(spike[i], uid[i], time, double.PositiveInfinity);
                        }
                    }
                }
            }
        }

        public void Add(List<double>[] ospike, List<int>[] ouid,
            List<double[,]> olfp, List<double> olfpstarttime,
            List<double>[] odintime, List<int>[] odinvalue,
            List<int> ocondindex, List<int> ocondrepeat, List<List<Dictionary<string, double>>> ocondstate)
        {
            lock (apilock)
            {
                if (ocondindex != null)
                {
                    nrecentct = ocondindex.Count;
                    if (condindex == null)
                    {
                        condindex = ocondindex;
                    }
                    else
                    {
                        condindex.AddRange(ocondindex);
                    }
                }
                else
                {
                    nrecentct = 0;
                }
                if (ocondrepeat != null)
                {
                    if (condrepeat == null)
                    {
                        condrepeat = ocondrepeat;
                    }
                    else
                    {
                        condrepeat.AddRange(ocondrepeat);
                    }
                }
                if (ocondstate != null)
                {
                    if (condstate == null)
                    {
                        condstate = ocondstate;
                    }
                    else
                    {
                        condstate.AddRange(ocondstate);
                    }
                }
                if (ospike != null)
                {
                    if (spike == null)
                    {
                        spike = ospike;
                        uid = ouid;
                    }
                    else
                    {
                        for (var i = 0; i < ospike.Length; i++)
                        {
                            spike[i].AddRange(ospike[i]);
                            uid[i].AddRange(ouid[i]);
                        }
                    }
                }
                if (odintime != null)
                {
                    if (dintime == null)
                    {
                        dintime = odintime;
                        dinvalue = odinvalue;
                        // The falling edge time of the first TTL pulse in begin-trigger-channel 
                        // marks the start of the experiment timer in VLab
                        var btchtime = dintime[btch];
                        if (btchtime.Count > 1)
                        {
                            vlabt0 = btchtime[1];
                        }
                    }
                    else
                    {
                        for (var i = 0; i < odintime.Length; i++)
                        {
                            dintime[i].AddRange(odintime[i]);
                            dinvalue[i].AddRange(odinvalue[i]);
                        }
                    }
                    if (!isdinmark)
                    {
                        isdinmark = dintime[markch].Count > 0;
                    }
                    if (isdinmark && !isdinmarkerror && condindex != null)
                    {
                        if (dinvalue[markch].Count == (condindex.Count * nmpc))
                        {
                            if (dinvalue[markch].InterMap((x, y) => x == y).Any(x => x))
                            {
                                isdinmarkerror = true;
                            }
                        }
                        else
                        {
                            isdinmarkerror = true;
                        }
                    }
                }
                if (condindex != null && nrecentct > 0)
                {
                    ParseCondOnOffTime();
                    ParseCondTestCond();
                }
            }
        }

        void ParseCondTestCond()
        {
            if (ex.Cond == null || ex.Cond.Count == 0) return;
            var nct = condindex.Count; var nc = condtestcond.Count == 0 ? 0 : condtestcond.Values.First().Count;
            if (nc < nct)
            {
                for (var i = nc; i < nct; i++)
                {
                    foreach (var f in ex.Cond.Keys.ToArray())
                    {
                        if (!condtestcond.ContainsKey(f))
                        {
                            condtestcond[f] = new List<object>();
                        }
                        condtestcond[f].Add(ex.Cond[f][condindex[i]]);
                    }
                    // Final Conditions
                    bool isori = condtestcond.ContainsKey("Ori");
                    bool isorioffset = condtestcond.ContainsKey("OriOffset");
                    var ori = (float)(isori ? condtestcond["Ori"][i] :
                        ex.EnvParam.ContainsKey("Ori") ? ex.EnvParam["Ori"] : 0);
                    var orioffset = (float)(isorioffset ? condtestcond["OriOffset"][i] :
                        ex.EnvParam.ContainsKey("OriOffset") ? ex.EnvParam["OriOffset"] : 0);
                    if (isori || isorioffset)
                    {
                        if (!condtestcond.ContainsKey("Ori_Final"))
                        {
                            condtestcond["Ori_Final"] = new List<object>();
                        }
                        condtestcond["Ori_Final"].Add(ori + orioffset);
                    }
                    bool isposition = condtestcond.ContainsKey("Position");
                    bool ispositionoffset = condtestcond.ContainsKey("PositionOffset");
                    bool isoripositionoffset = ex.EnvParam.ContainsKey("OriPositionOffset") ? (bool)ex.EnvParam["OriPositionOffset"] : false;
                    var position = (Vector3)(isposition ? condtestcond["Position"][i] :
                        ex.EnvParam.ContainsKey("Position") ? ex.EnvParam["Position"] : Vector3.zero);
                    var positionoffset = (Vector3)(ispositionoffset ? condtestcond["PositionOffset"][i] :
                        ex.EnvParam.ContainsKey("PositionOffset") ? ex.EnvParam["PositionOffset"] : Vector3.zero);
                    if (isposition || ispositionoffset)
                    {
                        var finalposition = position + (isoripositionoffset ? positionoffset.RotateZCCW(ori + orioffset) : positionoffset);
                        if (!condtestcond.ContainsKey("Position_Final"))
                        {
                            condtestcond["Position_Final"] = new List<object>();
                        }
                        condtestcond["Position_Final"].Add(finalposition);
                    }
                }
            }
        }

        bool TrySearchMarkTime(double tssearchpoint, double tesearchpoint, out double ts, out double te)
        {
            List<double> dinmarktime = dintime[markch]; List<int> dinmarkvalue = dinvalue[markch];
            int msv = dinmarkvalue[0];
            double dts, dte; List<int> tssidx = new List<int>(); List<int> tesidx = new List<int>();
            for (var i = dinmarktime.Count - 1; i >= 0; i--)
            {
                dts = dinmarktime[i] - tssearchpoint;
                dte = dinmarktime[i] - tesearchpoint;
                if (dinmarkvalue[i] == msv)
                {
                    if (Math.Abs(dts) <= msr)
                    {
                        tssidx.Add(i);
                    }
                }
                else
                {
                    if (Math.Abs(dte) <= msr)
                    {
                        tesidx.Add(i);
                    }
                }
                if (dts < -msr && dte < -msr)
                {
                    break;
                }
            }
            if (tssidx.Count == 1 && tesidx.Count == 1)
            {
                ts = dinmarktime[tssidx[0]];
                te = dinmarktime[tesidx[0]];
                return true;
            }
            else
            {
                ts = 0; te = 0; return false;
            }
        }

        void ParseCondOnOffTime()
        {
            var nct = condindex.Count; var nt = condontime.Count;
            if (nt < nct)
            {
                for (var i = nt; i < nct; i++)
                {
                    double ts, te;
                    if (isdinmark)
                    {
                        if (!isdinmarkerror)
                        {
                            ts = dintime[markch][i * nmpc] + ex.Delay;
                            te = dintime[markch][i * nmpc + 1] + ex.Delay;
                        }
                        else
                        {
                            var tss = condstate[i].FindStateTime(CONDSTATE.COND.ToString()) * (1 + ex.TimerDriftSpeed) +
                            vlabt0 + ex.Latency;
                            var tes = condstate[i].FindStateTime(CONDSTATE.SUFICI.ToString()) * (1 + ex.TimerDriftSpeed) +
                            vlabt0 + ex.Latency;
                            if (!TrySearchMarkTime(tss, tes, out ts, out te))
                            {
                                ts = tss;
                                te = tes;
                            }
                            ts += ex.Delay;
                            te += ex.Delay;
                        }
                    }
                    else
                    {
                        ts = condstate[i].FindStateTime(CONDSTATE.COND.ToString()) * (1 + ex.TimerDriftSpeed) +
                            vlabt0 + ex.Latency + ex.Delay;
                        te = condstate[i].FindStateTime(CONDSTATE.SUFICI.ToString()) * (1 + ex.TimerDriftSpeed) +
                            vlabt0 + ex.Latency + ex.Delay;
                    }
                    condontime.Add(ts); condofftime.Add(te);
                }
                // None-ICI Mark Mode
                if (ex.PreICI == 0 && ex.SufICI == 0)
                {
                    for (var i = nt; i < nct - 1; i++)
                    {
                        var currentontime = condontime[i];
                        var nextontime = condontime[i + 1];
                        if (nextontime - currentontime > (ex.CondDur + 2 * msr))
                        {
                            condofftime[i] = currentontime + ex.CondDur;
                        }
                        else
                        {
                            condofftime[i] = nextontime;
                        }
                    }
                    condofftime[nct - 1] = condontime[nct - 1] + ex.CondDur;
                }
            }
        }

        public bool IsData(int electrodeidx, SignalType signaltype)
        {
            lock (apilock)
            {
                switch (signaltype)
                {
                    case SignalType.Spike:
                        if (spike == null)
                        {
                            return false;
                        }
                        else
                        {
                            return spike[electrodeidx].Count > 0;
                        }
                    default:
                        return false;
                }
            }
        }
    }
}
