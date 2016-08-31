/*
VLAExtention.cs is part of the VLAB project.
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

namespace VLabAnalysis
{
    public static class VLAExtention
    {
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
            if (start > end) return;

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
                var ei = st.FindIndex(i => i >= end);
                if(ei>=0)
                {
                    var l = st.Count;
                    st.RemoveRange(ei, l - ei);
                    if (uid != null)
                    {
                        uid.RemoveRange(0, si);
                        uid.RemoveRange(ei, l - ei);
                    }
                }
            }
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

        public static double MFR(this List<double> st, double start, double end)
        {
            if (end > start)
            {
                return st.Count(start, end) / ((end - start) / 1000);
            }
            return 0;
        }

        public static double SEM(this List<double> x)
        {
            return x.StandardDeviation() / Math.Sqrt(x.Count);
        }

        public static string GetFactorUnit(this string factorname,out List<bool> valuedim,string exid="")
        {
            valuedim = new List<bool> { true };
            switch (factorname)
            {
                case "Diameter":
                case "Ori":
                    return factorname+" (Deg)";
                case "SpatialFreq":
                    return factorname+" (Cycle/Deg)";
                case "TemporalFreq":
                    return factorname+ " (Cycle/Sec)";
                case "PositionOffset":
                    if (exid.StartsWith("RF"))
                    {
                        if (exid.Contains('X'))
                        {
                            valuedim = new List<bool> { true, false, false };
                            return factorname + "_X (Deg)";
                        }
                        else if (exid.Contains('Y'))
                        {
                            valuedim = new List<bool> { false, true, false };
                            return factorname + "_Y (Deg)";
                        }
                        else if (exid.Contains('Z'))
                        {
                            valuedim = new List<bool> { false, false, true };
                            return factorname + "_Z (Deg)";
                        }
                        else
                        {
                            valuedim = new List<bool> { true, true, false };
                            return factorname + " (Deg)";
                        }
                    }
                    else
                    {
                        valuedim = new List<bool> { true, true, true };
                        return factorname + " (Deg)";
                    }
                default:
                    return factorname;
            }
        }

        public static List< T[]> GetFactorLevel<T>(this IEnumerable<object> vs, List<bool> valuedim)
        {
            var dn = valuedim.Count;
            if (dn <= 1)
            {
                return new List<T[]> {  vs.Select(i=>i.Convert<T>()).ToArray()} ;
            }
            else
            {
                var v = new List<T[]>();
                for (var i = 0; i < dn; i++)
                {
                    if (valuedim[i])
                    {
                        v.Add(vs.Select(j => ((object[])j)[i].Convert<T>()).ToArray());
                    }
                    else
                    {
                        v.Add(null);
                    }
                }
                return v;
            }
        }

        public static List<T> GetFactorLevel<T>(this object value, List<bool> valuedim)
        {
            var dn = valuedim.Count;
            if (dn <= 1)
            {
                return new List<T> { value.Convert<T>() };
            }
            else
            {
                var v = new List<T>();
                for (var i = 0; i < dn; i++)
                {
                    if (valuedim[i])
                    {
                        v.Add(((object[])value)[i].Convert<T>());
                    }
                }
                return v;
            }
        }

        public static List<double>[] GetFactorLevel(this IEnumerable<object> vs,string factorname,string exid)
        {
            switch(factorname)
            {
                case "PositionOffset":
                    if(exid.Contains('X'))
                    {
                        return new List<double>[] { vs.Select(i => ((object[])i)[0].Convert<double>()).ToList() };
                    }
                    else if(exid.Contains('Y'))
                    {
                        return new List<double>[] { vs.Select(i => ((object[])i)[1].Convert<double>()).ToList() };
                    }
                    else if (exid.Contains('Z'))
                    {
                        return new List<double>[] { vs.Select(i => ((object[])i)[2].Convert<double>()).ToList() };
                    }
                    else
                    {
                        return new List<double>[] { vs.Select(i => ((object[])i)[0].Convert<double>()).ToList(),
                        vs.Select(i => ((object[])i)[1].Convert<double>()).ToList(),
                        vs.Select(i => ((object[])i)[2].Convert<double>()).ToList()};
                    }
                default:
                    return new List<double>[] { vs.Select(i => i.Convert<double>()).ToList() };
            }
        }

        public static string GetResponseUnit(this ResultType type)
        {
            switch(type)
            {
                case ResultType.MFRResult:
                    return "Mean Firing Rate (spike/s)";
                default:
                    return "Response";
            }
        }

    }
}
