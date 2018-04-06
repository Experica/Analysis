using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections;
using VLab;

public class VLYamlTests
{
    string yaml = "Ori: [!!float 0, 45, 90, 135]\n" +
         "SpatialPhase: [0, 0.25, 0.5, 0.75]";

    [Test]
    public void YamlReadWrite()
    {
        var cond = yaml.DeserializeYaml<Dictionary<string, List<object>>>();
        cond["Position"] = new List<object> { Vector3.zero, Vector3.one };
        var syaml = cond.SerializeYaml();
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
