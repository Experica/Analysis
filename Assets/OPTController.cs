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
        int el, unit, mindatapoints, currentnode;

        public OPTController() {
            el = 5;
            unit = 0;
            mindatapoints = 5;
            currentnode = -1;
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
            ControlResult command;
            var ci = result.DataSet.CondIndex;
            int nci = result.DataSet.Ex.Cond.Values.Select(i => i.Count).Aggregate((total, next) => total * next); // not a great way of counting unique factor levels
            if (unitresponses.Count != ci.Count) return;

            // Start node
            if (currentnode < 0) currentnode = ci[0];
            var noderesponses = EvalNode(currentnode, unitresponses, ci);
            if (noderesponses.Count < mindatapoints)
            {
                command = new ControlResult(currentnode);
                controlresultqueue.Enqueue(command);
                return;
            }

            // Check neighboring nodes
            int nextnode = -1;
            double nexteval = noderesponses.Mean();
            List<int> neighbors = GetNeighbors(currentnode, nci);
            foreach (int n in neighbors) {
                var neighborresponses = EvalNode(n, unitresponses, ci);
                if (neighborresponses.Count < mindatapoints) { // neighbor doesn't have enough data
                    command = new ControlResult(n);
                    controlresultqueue.Enqueue(command);
                    return;
                } else // compare neighbor's score to ours
                {
                    if (neighborresponses.Mean() > nexteval) {  // neighbor scores higher than all others
                        nexteval = neighborresponses.Mean();
                        nextnode = n;
                    }
                }
            }

            if (nextnode == -1)
            {
                // No better nodes were found
                MersenneTwister rng = new MersenneTwister();
                nextnode = rng.Next(nci-1); // rng would be better
                currentnode = nextnode;
            }
            else
            {
                currentnode = nextnode;
                List<int> newneighbors = GetNeighbors(currentnode, nci);
                nextnode = newneighbors[0];
            }
            command = new ControlResult(nextnode);
            controlresultqueue.Enqueue(command);
        }

        protected List<int> GetNeighbors(int node, int nci)
        {
            // for now assume neighboring ci's are real neighbors
            List<int> n = new List<int>();
            n.Add(node - 1 < 0 ? nci - 1 : node - 1);
            n.Add(node + 1 > nci - 1 ? 0 : node + 1);
            return n;
        }

        protected List<double> EvalNode(int node, List<double> unitresponses, List<int> ci)
        {
            List<double> y = new List<double>();
            int nct = ci.Count;
            var cis = Enumerable.Range(0, nct).Where(i => ci[i] == node).ToList();
            if (cis.Count == 0) return y;
            foreach (var i in cis)
            {
                y.Add(unitresponses[i]);
            }
            return y;
        }


        public void Reset()
        {
            controlresultqueue = new ConcurrentQueue<IControlResult>();
            currentnode = -1;
        }

    }

    class ControlResult : IControlResult
    {
        public int idx;

        public ControlResult(int idxin)
        {
            idx = idxin;
        }

        public int CTIdx { get { return idx; } }
    }
}