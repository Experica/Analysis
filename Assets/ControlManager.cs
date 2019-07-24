/*
ControlManager.cs is part of the Experica.
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
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;
using System.Linq;

namespace Experica.Analysis
{
    [NetworkSettings(channel = 0, sendInterval = 0)]
    public class ControlManager : NetworkBehaviour
    {
        public UIController uicontroller;
        public IAnalysis als;

        [Command]
        public void CmdRF()
        {
        }

        [Command]
        public void CmdManualCTIndex(int idx)
        {
        }

        //[Command]
        //public void CmdNotifyUpdate()
        //{
        //    if (als == null) return;
        //}

        void ParseControlResult (IControlResult command)
        {
            if (command.GetType() == typeof(ControlResult))
            {
                CmdManualCTIndex(command.CTIdx);
            }
        }

        void Update()
        {
            if (als == null) return;
            if (als.Signal != null && als.Analyzers != null)
            {
                // sequential analysis in rank order: high to low
                var ranks = als.Analyzers.Keys.ToArray();
                Array.Sort(ranks, (i, j) => j.CompareTo(i));
                ConcurrentDictionary<Guid, IAnalyzer> ra;
                foreach (var rank in ranks)
                {
                    if (als.Analyzers.TryGetValue(rank, out ra) && ra != null)
                    {
                        foreach (var a in ra.Values.ToArray())
                        {
                            if (a != null)
                            {
                                IControlResult command;
                                if (a.Controller.ControlResultQueue.TryDequeue(out command))
                                {
                                    ParseControlResult(command);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
