/*
VLAExtention.cs is part of the VLAB project.
Copyright (c) 2017 Li Alex Zhang and Contributors

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
using OxyPlot;
using MathNet.Numerics.Statistics;

namespace VLabAnalysis
{
    public static class VLAExtention
    {
        public static readonly ConcurrentDictionary<int, OxyColor> Unit5Colors;

        static VLAExtention()
        {
            Unit5Colors = GetUnitColors(5);
        }

        public static ISignal SearchSignal()
        {
            foreach (var ss in Enum.GetValues(typeof(SignalSource)))
            {
                var s = SearchSignal((SignalSource)ss);
                if (s != null)
                {
                    return s;
                }
            }
            return null;
        }

        public static ISignal SearchSignal(SignalSource source)
        {
            ISignal s = null;
            switch (source)
            {
                case SignalSource.Ripple:
                    s = new RippleSignal();
                    break;
            }
            if (s != null && s.IsOnline)
            {
                return s;
            }
            else
            {
                return null;
            }
        }

        public static IAnalysis GetAnalysisSystem(this AnalysisSystem analysissystem, int cleardataperanalysis = 1,
            int retainanalysisperclear = 1, int sleepresolution = 2)
        {
            switch (analysissystem)
            {
                default:
                    return new DotNetAnalysis(cleardataperanalysis, retainanalysisperclear, sleepresolution);
            }
        }

        public static Type[] FindAll(this AnalysisInterface i)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var ts = assemblies.Where(a => a.GetName().Name == "Assembly-CSharp").SelectMany(a => a.GetTypes())
                .Where(t => t.Namespace == "VLabAnalysis" && t.IsClass && t.GetInterface(i.ToString()) != null).ToArray();
            return ts;
        }

        public static IAnalyzer GetAnalyzer(this int electrodeid, SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Spike:
                    return new MFRAnalyzer(new Signal(electrodeid, signaltype));
                default:
                    return null;
            }
        }

        public static IAnalyzer GetAnalyzer(this Type analyzertype, int electrodeid, SignalType signaltype)
        {
            if (typeof(IAnalyzer).IsAssignableFrom(analyzertype))
            {
                return (IAnalyzer)Activator.CreateInstance(analyzertype, new Signal(electrodeid, signaltype));
            }
            return null;
        }

        public static IEnumerable<T> InterMap<T>(this IEnumerable<T> s, Func<T, T, T> f)
        {
            var c = s.Count();
            if (c < 2)
            {
                yield return s.ElementAt(1);
            }
            else
            {
                for (var i = 0; i < s.Count() - 1; i++)
                {
                    yield return f(s.ElementAt(i), s.ElementAt(i + 1));
                }
            }
        }

        public static int Count(this List<double> st, double start, double end)
        {
            if (start >= end) return 0;

            int c = 0;
            foreach (var t in st)
            {
                if (t >= start && t < end)
                {
                    c++;
                }
            }
            return c;
        }

        public static void Sub(List<double> st, List<int> uid, double start, double end)
        {
            if (start >= end) return;

            var si = st.FindIndex(i => i >= start);
            if (si < 0)
            {
                st.Clear();
                if (uid != null)
                {
                    uid.Clear();
                }
            }
            else
            {
                st.RemoveRange(0, si);
                if (uid != null)
                {
                    uid.RemoveRange(0, si);
                }
                var ei = st.FindIndex(i => i >= end);
                if (ei >= 0)
                {
                    var l = st.Count - ei;
                    st.RemoveRange(ei, l);
                    if (uid != null)
                    {
                        uid.RemoveRange(ei, l);
                    }
                }
            }
        }

        public static List<double> GetUnitSpike(this List<double> st, List<int> uids, int uid)
        {
            var v = new List<double>();
            for (var i = 0; i < uids.Count; i++)
            {
                if (uids[i] == uid)
                {
                    v.Add(st[i]);
                }
            }
            return v;
        }

        public static double FindStateTime(this List<Dictionary<string, double>> stateevents, string state)
        {
            foreach (var e in stateevents)
            {
                if (e.ContainsKey(state))
                {
                    return e[state];
                }
            }
            return 0;
        }

        public static double MFR(this List<double> st, double start, double end, int timeunitpersec = 1000)
        {
            if (end > start)
            {
                return st.Count(start, end) / ((end - start) / timeunitpersec);
            }
            return 0;
        }

        public static double SEM(this List<double> x)
        {
            return x.StandardDeviation() / Math.Sqrt(x.Count);
        }

        public static string GetFinalFactor(this string factorname)
        {
            switch (factorname)
            {
                case "Ori":
                case "OriOffset":
                    return "Ori_Final";
                case "Position":
                case "PositionOffset":
                    return "Position_Final";
                default:
                    return factorname;
            }
        }

        public static string GetFactorInfo(this string factorname, object factorvalue,
            out int valuendim, out int[] valuevdimidx, out string[] valuevdimunit, out Type valuetype, out Type valueelementtype, string exid)
        {
            valuetype = factorvalue.GetType();
            string unit;
            switch (factorname)
            {
                case "Diameter":
                case "Ori":
                case "OriOffset":
                case "Ori_Final":
                    valuendim = 1;
                    valuevdimidx = new[] { 0 };
                    valuetype = typeof(float);
                    valueelementtype = null;
                    unit = "Deg";
                    break;
                case "SpatialFreq":
                    valuendim = 1;
                    valuevdimidx = new[] { 0 };
                    valuetype = typeof(float);
                    valueelementtype = null;
                    unit = "Cycle/Deg";
                    break;
                case "TemporalFreq":
                    valuendim = 1;
                    valuevdimidx = new[] { 0 };
                    valuetype = typeof(float);
                    valueelementtype = null;
                    unit = "Cycle/Sec";
                    break;
                case "Position":
                case "PositionOffset":
                case "Position_Final":
                    valuendim = 3;
                    valuetype = typeof(Vector3);
                    valueelementtype = typeof(float);
                    unit = "Deg";
                    if (!string.IsNullOrEmpty(exid) && exid.StartsWith("RF"))
                    {
                        if (exid.Contains('X'))
                        {
                            valuevdimidx = new[] { 0 };
                        }
                        else if (exid.Contains('Y'))
                        {
                            valuevdimidx = new[] { 1 };
                        }
                        else if (exid.Contains('Z'))
                        {
                            valuevdimidx = new[] { 2 };
                        }
                        else
                        {
                            valuevdimidx = new[] { 0, 1 };
                        }
                    }
                    else
                    {
                        valuevdimidx = new[] { 0, 1, 2 };
                    }
                    break;
                default:
                    unit = "";
                    if (valuetype == typeof(Vector2))
                    {
                        valuendim = 2;
                        valuevdimidx = new[] { 0, 1 };
                        valueelementtype = typeof(float);
                    }
                    else if (valuetype == typeof(Vector3))
                    {
                        valuendim = 3;
                        valuevdimidx = new[] { 0, 1, 2 };
                        valueelementtype = typeof(float);
                    }
                    else if (valuetype == typeof(float) || valuetype == typeof(string))
                    {
                        valuendim = 1;
                        valuevdimidx = new[] { 0 };
                        valueelementtype = null;
                    }
                    else
                    {
                        var v = (object[])factorvalue;
                        valuendim = v.Length;
                        valuevdimidx = Enumerable.Range(0, valuendim).ToArray();
                        valueelementtype = v[0].GetType();
                    }
                    break;
            }
            if (valueelementtype == null)
            {
                valuevdimunit = new[] { "" };
            }
            else
            {
                var vdn = valuevdimidx.Length;
                valuevdimunit = new string[vdn];
                for (var i = 0; i < vdn; i++)
                {
                    var dimidx = valuevdimidx[i];
                    valuevdimunit[i] = valuetype.GetFactorDimUnit(dimidx);
                }
            }
            return unit;
        }

        public static string GetFactorDimUnit(this Type vt, int dimidx)
        {
            if (vt == typeof(Vector3))
            {
                return dimidx == 0 ? "X" : dimidx == 1 ? "Y" : "Z";
            }
            else
            {
                return "";
            }
        }

        public static List<object>[] GetFactorValues(this IEnumerable<object> vs, Type vt, Type vet, int[] valuedimidx, int valuendim, out Type valuetype, out string[] dimunits)
        {
            if (vet == null)
            {
                valuetype = vt;
                dimunits = new[] { "" };
                return new[] { vs.Select(i => i.Convert(vt)).ToList() };
            }
            else
            {
                var valuevdimidx = valuedimidx.Where(i => i < valuendim).ToArray();
                var vdn = valuevdimidx.Length;
                if (vdn > 0)
                {
                    valuetype = vet;
                    dimunits = new string[vdn];
                    var v = new List<object>[vdn];
                    for (var i = 0; i < vdn; i++)
                    {
                        var dimidx = valuevdimidx[i];
                        dimunits[i] = vt.GetFactorDimUnit(dimidx);
                        v[i] = vs.Select(j => ((object[])j)[dimidx].Convert(vet)).ToList();
                    }
                    return v;
                }
                else
                {
                    valuetype = null;
                    dimunits = null;
                    return null;
                }
            }
        }

        public static string GetResultTitle(this IResult result)
        {
            var rt = result.GetType();
            if (rt == typeof(MFRResult))
            {
                return "Mean Firing Rate (spike/s)";
            }
            else
            {
                return "Response";
            }
        }

        public static string JoinFactorTitle(this string factor, string dimunit, string factorunit)
        {
            var ft = "";
            if (!string.IsNullOrEmpty(factorunit))
            {
                ft = factor;
                if (!string.IsNullOrEmpty(dimunit))
                {
                    ft = ft + "_" + dimunit;
                }
                if (!string.IsNullOrEmpty(factorunit))
                {
                    ft = ft + " (" + factorunit + ")";
                }
            }
            return ft;
        }

        public static ConcurrentDictionary<int, OxyColor> GetUnitColors(int ncolor = 5)
        {
            var cs = new ConcurrentDictionary<int, OxyColor>();
            for (var i = 1; i <= ncolor; i++)
            {
                cs[i] = OxyColor.FromHsv((double)(i - 1) / ncolor, 0.8, 1);
            }
            cs[0] = OxyColors.Black;
            return cs;
        }

    }
}
