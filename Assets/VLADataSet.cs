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
    /// Thread safe data container for an experiment
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

        List<int> _condindex ;
        List<List<Dictionary<string, double>>> _event;
        List<List<string>> _syncevent;

        Dictionary<string, List<List<double>>> _eventtime = new Dictionary<string, List<List<double>>>();
        List<string> _synceventstream = new List<string>();
        List<int> _synceventstreamctidx = new List<int>();

        List<double> condontime, condofftime;
        Dictionary<string, List<object>> condtestcond;

        int nctpull; double _vlabt0; bool isdineventsync,isdineventmeasure,isdineventsyncerror,isdineventmeasureerror;
        List<double> dineventsynctime,  dineventmeasuretime ;
        List<int> dineventsyncvalue, dineventmeasurevalue;
        readonly object apilock = new object();

        public VLADataSet()
        {
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

                _condindex = null;
                _syncevent = null;
                _event = null;
                condontime = new List<double>();
                condofftime = new List<double>();
                condtestcond = new Dictionary<string, List<object>>();

                nctpull = 0;
                _vlabt0 = 0;
                isdineventsync = false;
                isdinmarkerror = false;
            }
            GC.Collect();
        }

        public Experiment Ex
        {
            get { lock (apilock) { return ex; } }
            set { lock (apilock) { ex = value; } }
        }

        public List<double>[] Spike { get { lock (apilock) { return spike; } } }

        public List<int>[] UID { get { lock (apilock) { return uid; } } }

        public List<double>[] DInTime { get { lock (apilock) { return dintime; } } }

        public List<int>[] DInValue { get { lock (apilock) { return dinvalue; } } }

        public List<int> CondIndex { get { lock (apilock) { return _condindex; } } }

        public List<List<Dictionary<string,double>>> Event { get { lock (apilock) { return _event; } } }

        public List<List<string>> SyncEvent { get { lock (apilock) { return _syncevent; } } }

        public double VLabTimeZero
        {
            get { lock (apilock) { return _vlabt0; } }
            set { lock(apilock) { _vlabt0 = value; } }
        }

        public double VLabTimeToDataTime(double vlabtime, bool adddatalatency = false)
        {
            lock (apilock)
            {
                return vlabtime.VLabTimeToRefTime(_vlabt0, ex == null ? 0 : ex.TimerDriftSpeed, adddatalatency ? DataLatency : 0);
            }
        }

        public double DataLatency
        {
            get
            {
                lock (apilock)
                {
                    if (ex != null)
                    {
                        return ex.DisplayLatency + ex.ResponseDelay+ ex.Config.MaxDisplayLatencyError + ex.Config.OnlineSignalLatency;
                    }
                    return 0;
                }
            }
        }

        public uint EventSyncDCh { get { lock (apilock) {return ex == null ? 1 : ex.Config.EventSyncCh; } } }

        public uint EventMeasureDCh { get { lock (apilock) { return ex == null ? 2 : ex.Config.EventMeasureCh; } } }

        public uint StartSyncDCh { get { lock (apilock) { return ex == null ? 3 : ex.Config.StartSyncCh; } } }

        public uint StopSyncDCh { get { lock (apilock) { return ex == null ? 4 : ex.Config.StopSyncCh; } } }



        public List<double> CondOnTime { get { lock (apilock) { return condontime; } } }

        public List<double> CondOffTime { get { lock (apilock) { return condofftime; } } }

        public Dictionary<string, List<object>> CondTestCond { get { lock (apilock) { return condtestcond; } } }

        

        

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
            List<int> ocondindex, List<List<Dictionary<string, double>>> oevent, List<List<string>> osyncevent)
        {
            lock (apilock)
            {
                // update condindex and nctpull
                if (ocondindex != null)
                {
                    nctpull = ocondindex.Count;
                    if (_condindex == null)
                    {
                        _condindex = ocondindex;
                    }
                    else
                    {
                        _condindex.AddRange(ocondindex);
                    }
                }
                else
                {
                    nctpull = 0;
                }

                if (oevent != null)
                {
                    if (_event == null)
                    {
                        _event = oevent;
                    }
                    else
                    {
                        _event.AddRange(oevent);
                    }
                }

                if (osyncevent != null)
                {
                    if (_syncevent == null)
                    {
                        _syncevent = osyncevent;
                    }
                    else
                    {
                        _syncevent.AddRange(osyncevent);
                    }
                }

                // update spike and its unit id
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
                        // The falling edge time of the first digital input pulse in startsync-triggerchannel 
                        // marks the start of the experiment timer in VLab
                        var sstime = dintime[StartSyncDCh];
                        if (sstime != null && sstime.Count > 1)
                        {
                            _vlabt0 = sstime[1];
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
                }

                if (_condindex != null && nctpull > 0)
                {
                    _ParseCondTest();
                    _ParseCondTestCond();
                }
            }
        }

        void _ParseCondTest()
        {
            if (_event == null || _syncevent == null) return;
            var nct = _condindex.Count;var ctsidx = nct - nctpull;var nsepull = 0;
            // Parse Sync Event VLab Timing
            for(var i=ctsidx;i<nct;i++)
            {
                var es = _event[i];
                var ses = _syncevent[i];
                if (es != null && ses!=null)
                {
                    foreach (var se in ses)
                    {
                        _synceventstream.Add(se);
                        _synceventstreamctidx.Add(i);
                        nsepull++;
                        var vse = "VLab_" + se;
                        if(!_eventtime.ContainsKey(vse))
                        {
                            _eventtime[vse] = new List<double>[nctpull].ToList();
                        }
                        else
                        {
                            _eventtime[vse].AddRange(new List<double>[nctpull]);
                        }
                        _eventtime[vse][i] = es.FindEventTime(se).Select(t=>t.VLabTimeToRefTime(_vlabt0,ex.TimerDriftSpeed)).ToList();
                    }
                }
            }
            // Check Sync Event Data
            if(dintime!=null)
            {
                if(ex.EventSyncProtocol.nSyncChannel==1&&ex.EventSyncProtocol.nSyncpEvent==1)
                {
                    if (!isdineventsync)
                    {
                        isdineventsync = dintime[EventSyncDCh].Count > 0;
                    }
                    if(isdineventsync)
                    {
                        if(dineventsynctime==null)
                        {
                            dineventsynctime = dintime[EventSyncDCh];
                            dineventsyncvalue = dinvalue[EventSyncDCh];
                        }
                        if(_synceventstream.Count<= dineventsyncvalue.Count && dineventsyncvalue.InterMap((i,j)=>i!=j).All(i=>i))
                        {
                            isdineventsyncerror = false;
                        }
                        else
                        {
                            isdineventsyncerror = true;
                        }
                    }

                    if (!isdineventmeasure)
                    {
                        isdineventmeasure = dintime[EventMeasureDCh].Count > 0;
                    }
                    if (isdineventmeasure)
                    {
                        if(dineventmeasuretime==null)
                        {
                            dineventmeasuretime = dintime[EventMeasureDCh];
                            dineventmeasurevalue = dinvalue[EventMeasureDCh];
                        }
                        if (_synceventstream.Count <= dineventmeasurevalue.Count && dineventmeasurevalue.InterMap((i, j) => i != j).All(i => i ))
                        {
                            isdineventmeasureerror = false;
                        }
                        else
                        {
                            isdineventmeasureerror = true;
                        }
                    }
                }
            }
            // Parse Syn Event Timing
            var nse = _synceventstream.Count;var sesidx = nse - nsepull;
            if(isdineventsync)
            {
                if(!isdineventsyncerror)
                {
                    for (var i = sesidx; i < nse; i++)
                    {
                        var se = "Sync_" + _synceventstream[i];
                        if(!_eventtime.ContainsKey(se))
                        {
                            _eventtime[se] = Enumerable.Range(0, nsepull).Select(t => new List<double>()).ToList();
                        }
                        else
                        {
                            _eventtime[se].AddRange(Enumerable.Range(0, nsepull).Select(t => new List<double>()));
                        }
                        _eventtime[se][_synceventstreamctidx[i]].Add(dineventsynctime[i]);
                    }
                }
                else
                {
                    SearchRecover(_eventtime, "VLab_", "Sync_", ctsidx, nctpull, dineventsynctime, 0, ex.Config.MaxDisplayLatencyError);
                }
            }
            // Parse Sync Event Measure Timing
            if (isdineventmeasure)
            {
                if (!isdineventmeasureerror)
                {
                    for (var i = sesidx; i < nse; i++)
                    {
                        var mse = "Measure_" + _synceventstream[i];
                        if (!_eventtime.ContainsKey(mse))
                        {
                            _eventtime[mse] = Enumerable.Range(0, nsepull).Select(t => new List<double>()).ToList();
                        }
                        else
                        {
                            _eventtime[mse].AddRange(Enumerable.Range(0, nsepull).Select(t => new List<double>()));
                        }
                        _eventtime[mse][_synceventstreamctidx[i]].Add(dineventmeasuretime[i]);
                    }
                }
                else
                {
                    SearchRecover(_eventtime, "Sync_", "Measure_", ctsidx, nctpull, dineventmeasuretime, ex.DisplayLatency, ex.Config.MaxDisplayLatencyError);
                }
            }
        }

        void SearchRecover(Dictionary<string,List<List<double>>> eventtimes,string from,string to,int ctsidx,int nctpull,List<double> data,double latency,double sr)
        {
            var fromnames = eventtimes.Keys.Where(i => i.StartsWith(from));
            foreach(var fromname in fromnames)
            {
                var toname = fromname.Replace(from, to);
                for(var i=ctsidx;i<ctsidx+nctpull;i++)
                {
                    var recovered = new List<double>();
                    var fromctses = eventtimes[fromname][i];
                    if(fromctses!=null&&fromctses.Count>0)
                    {
                        foreach(var set in fromctses)
                        {
                            recovered.Add((set + latency).TrySearchTime(data, sr));
                        }
                    }
                    if (recovered.Count>0 && recovered.All(t=>t==double.NaN))
                    {
                        recovered.Clear();
                    }
                    eventtimes[toname][i] = recovered;
                }
            }
        }

        void _ParseCondTestCond()
        {
            if (ex.Cond == null || ex.Cond.Count == 0) return;
        }

        void ParseCondTestCond1()
        {
            if (ex.Cond == null || ex.Cond.Count == 0) return;
            var nct = _condindex.Count; var nc = condtestcond.Count == 0 ? 0 : condtestcond.Values.First().Count;
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
                        condtestcond[f].Add(ex.Cond[f][_condindex[i]]);
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
            var nct = _condindex.Count; var nt = condontime.Count;
            if (nt < nct)
            {
                for (var i = nt; i < nct; i++)
                {
                    double ts, te;
                    if (isdineventsync)
                    {
                        if (!isdinmarkerror)
                        {
                            ts = dintime[markch][i * nmpc] + ex.ResponseDelay;
                            te = dintime[markch][i * nmpc + 1] + ex.ResponseDelay;
                        }
                        else
                        {
                            var tss = _event[i].FindEventTime(CONDSTATE.COND.ToString()) * (1 + ex.TimerDriftSpeed) +
                            _vlabt0 + ex.DisplayLatency;
                            var tes = _event[i].FindEventTime(CONDSTATE.SUFICI.ToString()) * (1 + ex.TimerDriftSpeed) +
                            _vlabt0 + ex.DisplayLatency;
                            if (!TrySearchMarkTime(tss, tes, out ts, out te))
                            {
                                ts = tss;
                                te = tes;
                            }
                            ts += ex.ResponseDelay;
                            te += ex.ResponseDelay;
                        }
                    }
                    else
                    {
                        ts = _event[i].FindEventTime(CONDSTATE.COND.ToString()) * (1 + ex.TimerDriftSpeed) +
                            _vlabt0 + ex.DisplayLatency + ex.ResponseDelay;
                        te = _event[i].FindEventTime(CONDSTATE.SUFICI.ToString()) * (1 + ex.TimerDriftSpeed) +
                            _vlabt0 + ex.DisplayLatency + ex.ResponseDelay;
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
