using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using VLab;
using MsgPack;
using MsgPack.Serialization;
using MathNet.Numerics.Random;

public class VLMsgPackTests
{
    MersenneTwister rng = new MersenneTwister();

    [Test]
    public void ExSerialization()
    {
        var ex = new Experiment();
        ex.Cond = new Dictionary<string, IList>()
        {
            { "Ori", new List<float>() { 0, 90 } },
            { "CondState", new List<int[]>(){ new[] {0,1 },new[] { 2,3} } },
            {"Position",new List<Vector3>(){Vector3.zero,Vector3.one} }
        };
        var fvos = new List<object> { 0, 1 };
        var fvs = Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(float))).AsList();
        fvos.ForEach(i => fvs.Add(i.Convert<float>()));
        ex.Cond["Factor"] = fvs;
        ex.EnvParam = new Dictionary<string, object>()
        {
            {"OriOffset",10f },
            {"PositionOffset",Vector3.right },
            {"Color",Color.red },
            {"CondIndex",new List<int>(){7,4,12} }
        };
        var s = new MemoryStream();
        VLMsgPack.ExSerializer.Pack(s, ex);
        s.Position = 0;
        var dex = VLMsgPack.ExSerializer.Unpack(s);
        for (var i = 0; i < ex.Cond.Values.First().Count; i++)
        {
            Assert.AreEqual(ex.Cond["Ori"][i], dex.Cond["Ori"][i]);
            Assert.AreEqual(ex.Cond["Position"][i], dex.Cond["Position"][i]);
            Assert.AreEqual(ex.Cond["Factor"][i], dex.Cond["Factor"][i]);
        }
        Assert.AreEqual(ex.EnvParam["OriOffset"], dex.EnvParam["OriOffset"]);
        Assert.AreEqual(ex.EnvParam["PositionOffset"], dex.EnvParam["PositionOffset"]);
        Assert.AreEqual(ex.EnvParam["Color"], dex.EnvParam["Color"]);
        for (var i = 0; i < ex.EnvParam["CondIndex"].AsList().Count; i++)
        {
            Assert.AreEqual(ex.EnvParam["CondIndex"].AsList()[i], dex.EnvParam["CondIndex"].AsList()[i]);
        }
    }

    [Test]
    public void UnitySerialization()
    {
        var maxrandom = 50;
        var vs = Enumerable.Range(0, 50).Select(i => new Vector3(rng.Next(maxrandom), rng.Next(maxrandom), rng.Next(maxrandom))).ToList();
        var vsserializer = MessagePackSerializer.Get<List<Vector3>>();
        var s = new MemoryStream();
        vsserializer.Pack(s, vs);
        s.Position = 0;
        var dvs = vsserializer.Unpack(s);
        for (var i = 0; i < vs.Count; i++)
        {
            Assert.AreEqual(vs[i], dvs[i]);
        }

        var cs = Enumerable.Range(0, 50).Select(i => new Color(rng.Next(maxrandom), rng.Next(maxrandom), rng.Next(maxrandom), rng.Next(maxrandom))).ToList();
        var csserializer = MessagePackSerializer.Get<List<Color>>();
        s = new MemoryStream();
        csserializer.Pack(s, cs);
        s.Position = 0;
        var dcs = csserializer.Unpack(s);
        for (var i = 0; i < cs.Count; i++)
        {
            Assert.AreEqual(cs[i], dcs[i]);
        }
    }

    // A UnityTest behaves like a coroutine in PlayMode
    // and allows you to yield null to skip a frame in EditMode
    [UnityTest]
    public IEnumerator NewTestScriptWithEnumeratorPasses()
    {
        // Use the Assert class to test conditions.
        // yield to skip a frame
        yield return null;
    }
}
