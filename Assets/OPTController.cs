/*
OPTController.cs is part of the Experica.
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
using System.Linq;
using System.Collections;
using System.Threading;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using MathNet.Numerics.Statistics;
using MathNet.Numerics.Random;

namespace Experica.Analysis
{
    public class OPTController : IController
    {
        int disposecount = 0;
        ConcurrentQueue<IControlResult> controlresultqueue = new ConcurrentQueue<IControlResult>();
        int el, unit;

        public OPTController() {
            el = 5;
            unit = 0;
        }

        ~OPTController()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposecount, 1) == 1) return;
            if (disposing)
            {
            }
        }

        public ConcurrentQueue<IControlResult> ControlResultQueue
        {
            get { return controlresultqueue; }
        }

        public void Control(IResult result)
        {
            if (result.SignalChannel != el) return;
            if (result.UnitCondTestResponse.Count == 0 || !result.UnitCondTestResponse.ContainsKey(unit)) return;
            List<double> unitresponses = result.UnitCondTestResponse[unit];

            var ci = result.DataSet.CondIndex;
            int nci = result.DataSet.Ex.Cond.Values.Select(i => i.Count).Aggregate((total, next) => total * next); // not a great way of counting unique factor levels
            int nct = ci.Count;
            if (unitresponses.Count != ci.Count) return;

            // Generate mfr and sem for each unique condition index
            List<double> y = new List<double>();
            List<double> yse = new List<double>();
            int maxncis = 0;
            for (var x = 0; x < nci; x++)
            {
                var cis = Enumerable.Range(0, nct).Where(i => ci[i] == i).ToList();
                if (cis.Count > maxncis) maxncis = cis.Count;
                var flur = new List<double>();
                foreach (var idx in cis)
                {
                    flur.Add(unitresponses[idx]);
                }
                if (cis.Count == 0)
                {
                    y[x] = -1;
                    yse[x] = 0;
                }
                else
                {
                    y[x] = flur.Mean();
                    yse[x] = flur.SEM();
                }
            }

            if (maxncis > result.DataSet.Ex.CondRepeat)
            {
                controlresultqueue.Enqueue(new StopControlResult());
                return;
            }

            // Untested indices have uniformly distributed weights; tested ones weighted according to mfr
            List<double> resp = y.Where(i => i != -1).ToList();
            double sum = resp.Sum();
            int nunresp = nci - resp.Count;
            double uniformprob = 1.0 / nci;
            double remainingprob = (1 - (1.0 * nunresp / nci));
            List<double> weights = y.Select(i => i == -1 ? uniformprob : remainingprob * i / sum).ToList();

            controlresultqueue.Enqueue(new IdxWeightControlResult(weights));
        }


        public void Reset()
        {
            controlresultqueue = new ConcurrentQueue<IControlResult>();
        }

    }

    class IdxWeightControlResult : IControlResult
    {
        Control ctl = new Control();
        
        public IdxWeightControlResult(List<double> weights)
        {
            ctl.Type = ControlType.SamplingDistribution;
            ctl.Param.Add("weights", weights);
        }

        public Control Ctl { get; set; }
    }

    class StopControlResult : IControlResult
    {
        Control ctl = new Control();

        public StopControlResult()
        {
            ctl.Type = ControlType.StopExperiment;
        }

        public Control Ctl { get; set; }
    }
}