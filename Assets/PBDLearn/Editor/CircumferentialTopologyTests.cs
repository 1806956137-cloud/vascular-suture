using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PBDLearn
{
    public class CircumferentialTopologyTests
    {
        private const int RadialSegments = 24;
        private const int AxialSegments = 20;

        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        [Test]
        public void CurvedTube_UsesLongitudinalUvInsteadOfLocalZ()
        {
            object[] arguments =
                InvokeStationRingBuilder(
                    BuildCurvedTubeWork(
                        breakLongitudinalIdentity: false),
                    out bool success);

            Assert.That(success, Is.True, arguments[4] as string);
            IList rings = arguments[1] as IList;
            int[] vertexStation = arguments[2] as int[];
            string summary = arguments[3] as string;

            Assert.That(rings, Is.Not.Null);
            Assert.That(rings.Count, Is.EqualTo(AxialSegments + 1));
            Assert.That(vertexStation, Is.Not.Null);
            Assert.That(
                vertexStation.Count(station => station >= 0),
                Is.EqualTo((AxialSegments + 1) * RadialSegments));
            Assert.That(vertexStation[0], Is.EqualTo(0));
            Assert.That(
                vertexStation[vertexStation.Length - 1],
                Is.EqualTo(AxialSegments));
            Assert.That(summary, Does.Contain("badTriangleSpans=0"));
            Assert.That(summary, Does.Contain("weakAdjacentPairs=0"));
            Assert.That(summary, Does.Contain("endpointOrientation=uv.v"));
        }

        [Test]
        public void CurvedTube_RejectsNonMonotonicLongitudinalIdentityAtRingLayer()
        {
            object[] arguments =
                InvokeStationRingBuilder(
                    BuildCurvedTubeWork(
                        breakLongitudinalIdentity: true),
                    out bool success);

            Assert.That(success, Is.False);
            Assert.That(
                arguments[4] as string,
                Does.Contain(
                    "topology station longitudinal order is not strictly increasing"));
        }

        private static object[] InvokeStationRingBuilder(
            object work,
            out bool success)
        {
            Type generatorType =
                typeof(ClothSimulationDriverDoubleLayer).Assembly.GetType(
                    "PBDLearn.DoubleLayerIncisionGenerator",
                    throwOnError: true);
            MethodInfo method =
                generatorType.GetMethod(
                    "TryBuildCircumferentialStationRings",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] arguments =
            {
                work,
                null,
                null,
                string.Empty,
                string.Empty,
            };
            success =
                (bool)method.Invoke(
                    null,
                    arguments);
            return arguments;
        }

        private static object BuildCurvedTubeWork(
            bool breakLongitudinalIdentity)
        {
            Type workType =
                typeof(ClothSimulationDriverDoubleLayer).Assembly.GetType(
                    "PBDLearn.DoubleLayerIncisionGenerator+MeshWorkData",
                    throwOnError: true);
            object work =
                Activator.CreateInstance(
                    workType,
                    nonPublic: true);
            IList vertices =
                (IList)workType
                    .GetField("vertices", InstanceFields)
                    .GetValue(work);
            IList uv =
                (IList)workType
                    .GetField("uv", InstanceFields)
                    .GetValue(work);
            IList triangles =
                (IList)workType
                    .GetField("triangles", InstanceFields)
                    .GetValue(work);
            workType
                .GetField("hasUV", InstanceFields)
                .SetValue(work, true);

            for (int ring = 0;
                 ring <= AxialSegments;
                 ring++)
            {
                double s =
                    ring / (double)AxialSegments;
                double centerX =
                    0.38 * Math.Sin(Math.PI * s);
                double centerZ =
                    1.0 - s +
                    0.12 * Math.Sin(2.0 * Math.PI * s);
                double dx =
                    0.38 * Math.PI *
                    Math.Cos(Math.PI * s);
                double dz =
                    -1.0 +
                    0.24 * Math.PI *
                    Math.Cos(2.0 * Math.PI * s);
                double tangentLength =
                    Math.Sqrt(dx * dx + dz * dz);
                double normalX = dz / tangentLength;
                double normalZ = -dx / tangentLength;
                float longitudinal =
                    breakLongitudinalIdentity &&
                    ring == AxialSegments / 2
                        ? 0.20f
                        : (float)s;

                for (int column = 0;
                     column < RadialSegments;
                     column++)
                {
                    double theta =
                        2.0 * Math.PI *
                        column / RadialSegments;
                    double sin = Math.Sin(theta);
                    double cos = Math.Cos(theta);
                    vertices.Add(
                        new Vector3(
                            (float)(
                                centerX +
                                0.17 * sin * normalX),
                            (float)(0.17 * cos),
                            (float)(
                                centerZ +
                                0.17 * sin * normalZ)));
                    uv.Add(
                        new Vector2(
                            column /
                            (float)RadialSegments,
                            longitudinal));
                }
            }

            for (int ring = 0;
                 ring < AxialSegments;
                 ring++)
            {
                for (int column = 0;
                     column < RadialSegments;
                     column++)
                {
                    int next =
                        (column + 1) %
                        RadialSegments;
                    int a =
                        ring *
                        RadialSegments +
                        column;
                    int b =
                        ring *
                        RadialSegments +
                        next;
                    int c =
                        (ring + 1) *
                        RadialSegments +
                        column;
                    int d =
                        (ring + 1) *
                        RadialSegments +
                        next;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(d);
                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }

            return work;
        }
    }
}
