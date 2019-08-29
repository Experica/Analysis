/*
IAnalysis.cs is part of the Experica.
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

namespace Experica.Analysis
{
    public enum AnalysisEngine
    {
        ConditionTest
    }

    public readonly struct AnalysisEvent
    {
        public readonly int Index;
        public readonly bool IsClear;
        public readonly double Time;

        public AnalysisEvent(int index = 0, bool isclear = false, double time = 0)
        {
            Index = index;
            IsClear = isclear;
            Time = time;
        }
    }

    /// <summary>
    /// Analysis Engine Interface
    /// </summary>
    public interface IAnalysis : IDisposable
    {
        ISignal Signal { get; set; }
        DataSet DataSet { get; }
        ConcurrentDictionary<int, ConcurrentDictionary<Guid, IAnalyzer>> Analyzers { get; }
        void AddAnalyzer(IAnalyzer analyzer, int rank);
        void RemoveAnalyzer(Guid id);
        int ClearDataPerAnalysis { get; set; }
        int RetainAnalysisPerClear { get; set; }
        int SleepDuration { get; set; }
        int ExperimentAnalysisStage { get; set; }
        bool IsAnalyzing { get; }
        int AnalysisEventCount { get; }
        int AnalysisDoneCount { get; }
        int VisualizationDoneCount { get; }
        void CondTestEnqueue(CONDTESTPARAM name, object value);
        void CondTestEndEnqueue(double time);
        void ExperimentEndEnqueue();
        void Start();
        void Stop();
        void Restart();
        void VisualizeResults(VisualizeMode mode);
        void SaveVisualization();
        void SaveVisualization(string dir, string name, int width, int height, int dpi);
        void LayoutVisualization(bool isfront, bool isalign);
    }

    public enum AnalysisInterface
    {
        IAnalyzer,
        IVisualizer,
        IController
    }

    public enum VisualizeMode
    {
        First,
        Last,
        All
    }

    public interface IAnalyzer : IDisposable
    {
        Guid ID { get; }
        SignalDescription SignalDescription { get; set; }
        IVisualizer Visualizer { get; set; }
        IController Controller { get; set; }
        ConcurrentQueue<IResult> ResultVisualizeQueue { get; }
        IResult Result { get; }
        void Reset();
        void Analyze(DataSet dataset);
    }

    public interface IResult
    {
        IResult Copy();
        int SignalChannel { get; }
        string ExperimentID { get; }
        DataSet DataSet { get; }
        Dictionary<int, List<double>> UnitCondTestResponse { get; }
    }

    public interface IController : IDisposable
    {
        void Control(IResult result);
        void Reset();
        ConcurrentQueue<IControlResult> ControlResultQueue { get; }
    }

    public interface IControlResult
    {
        Control Ctl { get; set; }
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
