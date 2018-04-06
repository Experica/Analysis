/*
IAnalysisInterface.cs is part of the VLAB project.
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
using System;
using System.Collections.Concurrent;

namespace VLabAnalysis
{
    public interface IAnalyzer : IDisposable
    {
        int ID { get; set; }
        Signal Signal { get; set; }
        void Analyze(VLADataSet dataset);
        IVisualizer Visualizer { get; set; }
        IController Controller { get; set; }
        ConcurrentQueue<IResult> ResultVisualizeQueue { get; }
        IResult Result { get; }
        void Reset();
    }

    public interface IResult
    {
        IResult DeepCopy();
        int SignalID { get; }
        string ExperimentID { get; }
        List<Dictionary<int, double>> CondResponse { get; }
        List<int> CondIndex { get; }
        List<int> CondRepeat { get; }
        Dictionary<string, List<object>> CondTestCond { get; }
        Dictionary<string, object> EnvParam { get; }
    }

    public interface IController : IDisposable
    {
        void Control(IResult result);
        void Reset();
        ConcurrentQueue<IControlResult> ControlResultQueue { get; }
    }

    public interface IControlResult
    {

    }

    public class UpdateCommand : IControlResult
    {

    }

    public interface IVisualizer : IDisposable
    {
        void Visualize(IResult result);
        void Reset();
        void Save(string path, int width, int height, int dpi);
        void ShowInFront();
        Vector2 Position { get; set; }
    }
}
