/*
DataSet.cs is part of the Experica.
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
using System.Collections.Immutable;
using System.Collections.Generic;
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

namespace Experica.Analysis
{
    public enum TimeVersion
    {
        None,
        Command,
        Sync,
        Measure
    }

    /// <summary>
    /// Thread safe data container for an experiment, internal data shouldn't be changed other than analysis engine
    /// </summary>
    public class DataSet
    {
        Experiment ex;
        AnalysisConfig config;
        ConcurrentDictionary<int, List<double>> spike;
        ConcurrentDictionary<int, List<int>> uid;
        List<double[,]> lfp;
        List<double> lfpstarttime;
        ConcurrentDictionary<int, List<double>> dintime;
        ConcurrentDictionary<int, List<int>> dinvalue;
        List<int> _condindex;
        List<List<Dictionary<string, double>>> _event;
        List<List<string>> _syncevent;

        Dictionary<string, List<List<double>>> _eventtime = new Dictionary<string, List<List<double>>>();
        List<string> _synceventstream = new List<string>();
        List<int> _synceventstreamctidx = new List<int>();
        List<double> condontime = new List<double>();
        List<double> condofftime = new List<double>();
        Dictionary<string, List<object>> condtestcond = new Dictionary<string, List<object>>();

        double _vlabt0; int nctpull;
        bool isaccuratevlabt0, isdineventsync, isdineventmeasure, isdineventsyncerror, isdineventmeasureerror;
        int eventsyncintegrity, eventmeasureintegrity;
        bool isvlabcondon, issynccondon, ismeasurecondon, isvlabcondoff, issynccondoff, ismeasurecondoff;
        readonly object apilock = new object();


        public DataSet()
        {
            Reset();
        }

        public void Reset()
        {
            lock (apilock)
            {
                ex = null;
                config = null;
                spike = null;
                uid = null;
                lfp = null;
                lfpstarttime = null;
                dintime = null;
                dinvalue = null;
                _condindex = null;
                _event = null;
                _syncevent = null;

                _eventtime.Clear();
                _synceventstream.Clear();
                _synceventstreamctidx.Clear();
                condontime.Clear();
                condofftime.Clear();
                condtestcond.Clear();

                _vlabt0 = 0;
                nctpull = 0;
                isaccuratevlabt0 = false;
                isdineventsync = false;
                isdineventmeasure = false;
                isdineventsyncerror = false;
                isdineventmeasureerror = false;
                Interlocked.Exchange(ref eventsyncintegrity, 1);
                Interlocked.Exchange(ref eventmeasureintegrity, 1);
                isvlabcondon = false;
                issynccondon = false;
                ismeasurecondon = false;
                isvlabcondoff = false;
                issynccondoff = false;
                ismeasurecondoff = false;
            }
            GC.Collect();
        }

        public Experiment Ex
        {
            get { lock (apilock) { return ex; } }
            set { lock (apilock) { ex = value; } }
        }

        public void ParseEx()
        {
            lock (apilock)
            {
                if (ex == null) return;
                if (ex.EnvParam != null)
                {

                }
                if (ex.Param != null)
                {

                }
                if (ex.Cond != null)
                {

                }
            }
        }

        public AnalysisConfig Config
        {
            get { lock (apilock) { return config; } }
            set { lock (apilock) { config = value; } }
        }

        public ConcurrentDictionary<int, List<double>> Spike { get { lock (apilock) { return spike; } } }

        public ConcurrentDictionary<int, List<int>> UID { get { lock (apilock) { return uid; } } }

        public ConcurrentDictionary<int, List<double>> DInTime { get { lock (apilock) { return dintime; } } }

        public ConcurrentDictionary<int, List<int>> DInValue { get { lock (apilock) { return dinvalue; } } }

        public List<int> CondIndex { get { lock (apilock) { return _condindex; } } }

        public List<List<Dictionary<string, double>>> Event { get { lock (apilock) { return _event; } } }

        public List<List<string>> SyncEvent { get { lock (apilock) { return _syncevent; } } }

        public Dictionary<string, List<List<double>>> EventTime { get { lock (apilock) { return _eventtime; } } }

        public List<string> SyncEventStream { get { lock (apilock) { return _synceventstream; } } }

        public List<int> SyncEventStreamCondTestIndex { get { lock (apilock) { return _synceventstreamctidx; } } }

        public List<double> CondOnTime { get { lock (apilock) { return condontime; } } }

        public List<double> CondOffTime { get { lock (apilock) { return condofftime; } } }

        public Dictionary<string, List<object>> CondTestCond { get { lock (apilock) { return condtestcond; } } }

        public double VLabTimeZero
        {
            get { lock (apilock) { return _vlabt0; } }
            set { lock (apilock) { _vlabt0 = value; } }
        }

        public bool IsAccurateVLabTimeZero { get { lock (apilock) { return isaccuratevlabt0; } } }

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
                        return ex.DisplayLatency + ex.ResponseDelay + ex.Config.MaxDisplayLatencyError + ex.Config.OnlineSignalLatency;
                    }
                    return 0;
                }
            }
        }

        public int EventSyncDCh { get { lock (apilock) { return ex == null ? 1 : (int)ex.Config.EventSyncCh + 1; } } }

        public int EventMeasureDCh { get { lock (apilock) { return ex == null ? 2 : (int)ex.Config.EventMeasureCh + 1; } } }

        public int StartSyncDCh { get { lock (apilock) { return ex == null ? 3 : (int)ex.Config.StartSyncCh + 1; } } }

        public int StopSyncDCh { get { lock (apilock) { return ex == null ? 4 : (int)ex.Config.StopSyncCh + 1; } } }

        public int EventSyncIntegrity { get { return Interlocked.CompareExchange(ref eventsyncintegrity, 0, int.MinValue); } }

        public int EventMeasureIntegrity { get { return Interlocked.CompareExchange(ref eventmeasureintegrity, 0, int.MinValue); } }

        public bool IsData(int electrodeid, SignalType signaltype)
        {
            lock (apilock)
            {
                switch (signaltype)
                {
                    case SignalType.Spike:
                        if (spike == null || !spike.ContainsKey(electrodeid))
                        {
                            return false;
                        }
                        else
                        {
                            return spike[electrodeid].Count > 0;
                        }
                    default:
                        return false;
                }
            }
        }

        public void Remove(double time)
        {
            lock (apilock)
            {
                if (spike != null)
                {
                    foreach (var e in spike.Keys.ToArray())
                    {
                        if (spike[e].Count > 0)
                        {
                            AnalysisExtention.Sub(spike[e], uid[e], time, double.PositiveInfinity);
                        }
                    }
                }
            }
        }

        public void Add(Dictionary<int, List<double>> ospike, Dictionary<int, List<int>> ouid,
            List<double[,]> olfp, List<double> olfpstarttime,
            Dictionary<int, List<double>> odintime, Dictionary<int, List<int>> odinvalue,
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
                        spike = new ConcurrentDictionary<int, List<double>>(ospike);
                        uid = new ConcurrentDictionary<int, List<int>>(ouid);
                    }
                    else
                    {
                        foreach (var e in ospike.Keys.ToArray())
                        {
                            if (!spike.ContainsKey(e))
                            {
                                spike[e] = new List<double>();
                                uid[e] = new List<int>();
                            }
                            spike[e].AddRange(ospike[e]);
                            uid[e].AddRange(ouid[e]);
                        }
                    }
                }

                if (odintime != null)
                {
                    if (dintime == null)
                    {
                        dintime = new ConcurrentDictionary<int, List<double>>(odintime);
                        dinvalue = new ConcurrentDictionary<int, List<int>>(odinvalue);
                        // The falling edge time of the first digital input pulse in startsync trigger channel 
                        // marks the start of the experiment timer in VLab
                        if (dintime.ContainsKey(StartSyncDCh))
                        {
                            var sstime = dintime[StartSyncDCh];
                            if (sstime != null && sstime.Count > 1)
                            {
                                _vlabt0 = sstime[1];
                                isaccuratevlabt0 = true;
                            }
                        }
                    }
                    else
                    {
                        foreach (var e in odintime.Keys.ToArray())
                        {
                            if (!dintime.ContainsKey(e))
                            {
                                dintime[e] = new List<double>();
                                dinvalue[e] = new List<int>();
                            }
                            dintime[e].AddRange(odintime[e]);
                            dinvalue[e].AddRange(odinvalue[e]);
                        }
                    }
                }

                if (_condindex != null && nctpull > 0)
                {
                    var nct = _condindex.Count; var ctsidx = nct - nctpull;
                    try
                    {
                        _ParseCondTest(nct, nctpull, ctsidx);
                        _ParseCondTestCond(nct, nctpull, ctsidx);
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        void _ParseCondTest(int nct, int nctpull, int ctsidx)
        {
            if (_event == null || _syncevent == null) return;
            var nsepull = 0;
            // Parse Sync Event VLab Timing
            for (var i = ctsidx; i < nct; i++)
            {
                var es = _event[i];
                var ses = _syncevent[i];
                if (es != null && ses != null)
                {
                    _synceventstream.AddRange(ses);
                    _synceventstreamctidx.AddRange(Enumerable.Repeat(i, ses.Count));
                    nsepull += ses.Count;
                    var usets = es.FindEventTime(ses).Select(t => t.VLabTimeToRefTime(_vlabt0, ex.TimerDriftSpeed)).ToList().UniqueEventTime(ses);
                    foreach (var se in usets.Keys)
                    {
                        var vse = "VLab_" + se;
                        if (!_eventtime.ContainsKey(vse))
                        {
                            _eventtime[vse] = new List<List<double>>();
                        }
                        var nvset = _eventtime[vse].Count;
                        if (nvset < i)
                        {
                            _eventtime[vse].AddRange(Enumerable.Repeat(default(List<double>), i - nvset));
                        }
                        _eventtime[vse].Add(usets[se]);
                    }
                }
            }
            // Check Sync Event Data
            if (dintime != null)
            {
                if (ex.EventSyncProtocol.nSyncChannel == 1 && ex.EventSyncProtocol.nSyncpEvent == 1)
                {
                    if (!isdineventsync && dintime.ContainsKey(EventSyncDCh))
                    {
                        isdineventsync = dintime[EventSyncDCh].Count > 0;
                    }
                    if (isdineventsync)
                    {
                        if (!isdineventsyncerror && _synceventstream.Count == dintime[EventSyncDCh].Count && dinvalue[EventSyncDCh].InterMap((i, j) => i != j).All(i => i))
                        {
                        }
                        else
                        {
                            isdineventsyncerror = true;
                            Interlocked.Exchange(ref eventsyncintegrity, 0);
                        }
                    }

                    if (!isdineventmeasure && dintime.ContainsKey(EventMeasureDCh))
                    {
                        isdineventmeasure = dintime[EventMeasureDCh].Count > 0;
                    }
                    if (isdineventmeasure)
                    {
                        if (!isdineventmeasureerror && _synceventstream.Count == dintime[EventMeasureDCh].Count && dinvalue[EventMeasureDCh].InterMap((i, j) => i != j).All(i => i))
                        {
                        }
                        else
                        {
                            isdineventmeasureerror = true;
                            Interlocked.Exchange(ref eventmeasureintegrity, 0);
                        }
                    }
                }
            }
            // Parse Sync Event Timing
            var nse = _synceventstream.Count; var sesidx = nse - nsepull;
            if (isdineventsync)
            {
                if (!isdineventsyncerror)
                {
                    for (var i = sesidx; i < nse; i++)
                    {
                        var se = "Sync_" + _synceventstream[i];
                        if (!_eventtime.ContainsKey(se))
                        {
                            _eventtime[se] = new List<List<double>>();
                        }
                        var nset = _eventtime[se].Count;
                        if (nset <= _synceventstreamctidx[i])
                        {
                            _eventtime[se].AddRange(Enumerable.Repeat(new List<double>(), nset - _synceventstreamctidx[i] + 1));
                        }
                        _eventtime[se][_synceventstreamctidx[i]].Add(dintime[EventSyncDCh][i]);
                    }
                }
                else
                {
                    SearchRecover(_eventtime, "VLab_", "Sync_", ctsidx, nct, dintime[EventSyncDCh], 0, ex.Config.MaxDisplayLatencyError);
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
                            _eventtime[mse] = new List<List<double>>();
                        }
                        var nmset = _eventtime[mse].Count;
                        if (nmset <= _synceventstreamctidx[i])
                        {
                            _eventtime[mse].AddRange(Enumerable.Repeat(new List<double>(), nmset - _synceventstreamctidx[i] + 1));
                        }
                        _eventtime[mse][_synceventstreamctidx[i]].Add(dintime[EventMeasureDCh][i]);
                    }
                }
                else
                {
                    SearchRecover(_eventtime, "Sync_", "Measure_", ctsidx, nct, dintime[EventMeasureDCh], ex.DisplayLatency, ex.Config.MaxDisplayLatencyError);
                }
            }
            // Try to get the most accurate and complete Cond On/Off Time
            if (!isvlabcondon) isvlabcondon = _eventtime.ContainsKey("VLab_COND");
            if (!issynccondon) issynccondon = _eventtime.ContainsKey("Sync_COND");
            if (!ismeasurecondon) ismeasurecondon = _eventtime.ContainsKey("Measure_COND");
            if (!isvlabcondoff) isvlabcondoff = _eventtime.ContainsKey("VLab_SUFICI");
            if (!issynccondoff) issynccondoff = _eventtime.ContainsKey("Sync_SUFICI");
            if (!ismeasurecondoff) ismeasurecondoff = _eventtime.ContainsKey("Measure_SUFICI");
            TimeVersion condonversion = TimeVersion.None; TimeVersion condoffversion = TimeVersion.None;
            List<List<double>> condon = null, condoff = null;
            if (ismeasurecondon)
            {
                condon = _eventtime["Measure_COND"].GetRange(ctsidx, nctpull).Select(ct => ct.ToList()).ToList(); // deep copy
                condonversion = TimeVersion.Measure;
            }
            else if (issynccondon)
            {
                condon = _eventtime["Sync_COND"].GetRange(ctsidx, nctpull).Select(ct => ct.Select(i => i + ex.DisplayLatency).ToList()).ToList();
                condonversion = TimeVersion.Sync;
            }
            else if (isvlabcondon)
            {
                condon = _eventtime["VLab_COND"].GetRange(ctsidx, nctpull).Select(ct => ct.Select(i => i + ex.DisplayLatency).ToList()).ToList();
                condonversion = TimeVersion.Command;
            }

            if (ismeasurecondoff)
            {
                condoff = _eventtime["Measure_SUFICI"].GetRange(ctsidx, nctpull).Select(ct => ct.ToList()).ToList();
                condoffversion = TimeVersion.Measure;
            }
            else if (issynccondoff)
            {
                condoff = _eventtime["Sync_SUFICI"].GetRange(ctsidx, nctpull).Select(ct => ct.Select(i => i + ex.DisplayLatency).ToList()).ToList(); ;
                condoffversion = TimeVersion.Sync;
            }
            else if (isvlabcondoff)
            {
                condoff = _eventtime["VLab_SUFICI"].GetRange(ctsidx, nctpull).Select(ct => ct.Select(i => i + ex.DisplayLatency).ToList()).ToList();
                condoffversion = TimeVersion.Command;
            }

            if (condon != null && condonversion == TimeVersion.Measure && isdineventmeasureerror && issynccondon)
            {
                CombineTime(_eventtime["Sync_COND"].GetRange(ctsidx, nctpull), condon, ex.DisplayLatency);
                condonversion = TimeVersion.Sync;
            }
            if (condoff != null && condoffversion == TimeVersion.Measure && isdineventmeasureerror && issynccondoff)
            {
                CombineTime(_eventtime["Sync_SUFICI"].GetRange(ctsidx, nctpull), condoff, ex.DisplayLatency);
                condoffversion = TimeVersion.Sync;
            }
            if (condon != null && condonversion == TimeVersion.Sync && isdineventsyncerror && isvlabcondon)
            {
                CombineTime(_eventtime["VLab_COND"].GetRange(ctsidx, nctpull), condon, ex.DisplayLatency);
                condonversion = TimeVersion.Command;
            }
            if (condoff != null && condoffversion == TimeVersion.Sync && isdineventsyncerror && isvlabcondoff)
            {
                CombineTime(_eventtime["VLab_SUFICI"].GetRange(ctsidx, nctpull), condoff, ex.DisplayLatency);
                condoffversion = TimeVersion.Command;
            }
            // Try to get first Cond On/Off Timing
            if (condon != null)
            {
                var non = condontime.Count;
                if (non < ctsidx)
                {
                    condontime.AddRange(Enumerable.Repeat(double.NaN, ctsidx - non));
                }
                condontime.AddRange(condon.EventFirstTime());
            }
            if (condoff != null)
            {
                var noff = condofftime.Count;
                if (noff < ctsidx)
                {
                    condofftime.AddRange(Enumerable.Repeat(double.NaN, ctsidx - noff));
                }
                condofftime.AddRange(condoff.EventFirstTime());
            }
            // Parse CondOff when no SufICI event found
            if (condon != null && condoff == null)
            {
                for (var i = ctsidx; i < nct - 1; i++)
                {
                    var currentontime = condontime[i];
                    var nextontime = condontime[i + 1];
                    if ((nextontime - currentontime) > (ex.CondDur + 2 * ex.Config.MaxDisplayLatencyError))
                    {
                        condofftime.Add(currentontime + ex.CondDur);
                    }
                    else
                    {
                        condofftime.Add(nextontime);
                    }
                }
                condofftime.Add(condontime[nct] + ex.CondDur);
            }
        }

        void SearchRecover(Dictionary<string, List<List<double>>> eventtimes, string from, string to, int ctsidx, int nct, List<double> data, double latency, double sr)
        {
            foreach (var fromname in eventtimes.Keys.Where(i => i.StartsWith(from)).ToArray())
            {
                var toname = fromname.Replace(from, to);
                if (!eventtimes.ContainsKey(toname))
                {
                    eventtimes[toname] = new List<List<double>>();
                }
                var ntoet = eventtimes[toname].Count;
                if (ntoet < ctsidx)
                {
                    eventtimes[toname].AddRange(Enumerable.Repeat<List<double>>(null, ctsidx - ntoet));
                }
                for (var i = ctsidx; i < nct; i++)
                {
                    var recovered = new List<double>();
                    var fromctes = eventtimes[fromname][i];
                    if (fromctes != null && fromctes.Count > 0)
                    {
                        recovered.AddRange(fromctes.Select(t => (t + latency).TrySearchTime(data, sr)));
                    }
                    if (recovered.Count > 0 && recovered.All(t => double.IsNaN(t)))
                    {
                        recovered.Clear();
                    }
                    eventtimes[toname].Add(recovered);
                }
            }
        }

        void CombineTime(List<List<double>> from, List<List<double>> to, double latency)
        {
            for (var i = 0; i < from.Count; i++)
            {
                var fromts = from[i];
                var tots = to[i];
                List<double> combined = null;
                if (tots == null || tots.Count == 0)
                {
                    if (fromts != null && fromts.Count > 0) combined = fromts.ToList(); // copy data
                }
                else
                {
                    combined = tots;
                    if (fromts != null)
                    {
                        for (var j = 0; j < combined.Count; j++)
                        {
                            if (double.IsNaN(combined[j]) && !double.IsNaN(fromts[j]))
                            {
                                combined[j] = fromts[j];
                            }
                        }
                    }
                }
                if (combined != null && combined.Count > 0 && combined.All(t => double.IsNaN(t)))
                {
                    combined = null;
                }
                to[i] = combined;
            }
        }

        void _ParseCondTestCond(int nct, int nctpull, int ctsidx)
        {
            if (ex.Cond == null || ex.Cond.Count == 0) return;
            for (var i = ctsidx; i < nct; i++)
            {
                foreach (var f in ex.Cond.Keys)
                {
                    if (!condtestcond.ContainsKey(f))
                    {
                        condtestcond[f] = new List<object>();
                    }
                    condtestcond[f].Add(ex.Cond[f][_condindex[i]]);
                }
                // Final Orientation
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
                // Final Position
                bool isposition = condtestcond.ContainsKey("Position");
                bool ispositionoffset = condtestcond.ContainsKey("PositionOffset");
                bool isoripositionoffset = ex.EnvParam.ContainsKey("OriPositionOffset") ? (bool)ex.EnvParam["OriPositionOffset"] : false;
                var position = (Vector3)(isposition ? condtestcond["Position"][i] :
                    ex.EnvParam.ContainsKey("Position") ? ex.EnvParam["Position"] : Vector3.zero);
                var positionoffset = (Vector3)(ispositionoffset ? condtestcond["PositionOffset"][i] :
                    ex.EnvParam.ContainsKey("PositionOffset") ? ex.EnvParam["PositionOffset"] : Vector3.zero);
                if (isposition || ispositionoffset)
                {
                    if (!condtestcond.ContainsKey("Position_Final"))
                    {
                        condtestcond["Position_Final"] = new List<object>();
                    }
                    condtestcond["Position_Final"].Add(position + (isoripositionoffset ? positionoffset.RotateZCCW(ori + orioffset) : positionoffset));
                }
            }
        }
    }
}
