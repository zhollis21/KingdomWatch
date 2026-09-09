using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests
{
    /// <summary>
    /// Guards the three properties of Core that the rest of the plan rests on:
    /// it is netstandard2.1, it is single-targeted, and it depends on nothing.
    ///
    /// These are cheap to satisfy today and expensive to notice the loss of
    /// later - a Unity type or a stray package reference inside Core does not
    /// break the build, it breaks the harness/Unity determinism story months
    /// afterwards. See docs/design/kingdom-watch-plan-v7.1.md section 5.
    /// </summary>
    [TestFixture]
    public sealed class CoreAssemblyContractTests
    {
        // Core exposes no public types yet, so there is nothing to reference at
        // compile time. The ProjectReference still copies the assembly to this
        // project's output, which is what makes the load succeed.
        private static Assembly CoreAssembly =>
            Assembly.Load(new AssemblyName("KingdomWatch.Core"));

        [Test]
        public void Core_targets_netstandard21_and_only_netstandard21()
        {
            // This also catches multi-targeting, which is the failure mode section 5
            // singles out. If Core ever grew a net10.0 target, this net10.0 test
            // project would resolve that build instead and the framework name
            // would come back as .NETCoreApp.
            var frameworkName = CoreAssembly
                .GetCustomAttribute<TargetFrameworkAttribute>()
                ?.FrameworkName;

            Assert.That(
                frameworkName,
                Is.EqualTo(".NETStandard,Version=v2.1"),
                "Core must stay single-targeted at netstandard2.1 - Unity does not "
                + "support managed plug-ins built for .NET Core, any version.");
        }

        [Test]
        public void Core_references_no_unity_assemblies()
        {
            var unityReferences = CoreAssembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(name => name is not null
                               && name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(
                unityReferences,
                Is.Empty,
                "Core must have zero Unity dependencies. NativeArray, Jobs and Burst "
                + "belong in a separate optimization backend, never in Core.");
        }

        [Test]
        public void Core_takes_no_third_party_dependencies()
        {
            // A netstandard2.1 library with no PackageReference resolves to the
            // single 'netstandard' facade. Anything else means a dependency was
            // added, which the zero-dependency rule does not allow.
            var referenced = CoreAssembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? "<unnamed>")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                referenced,
                Is.EqualTo(new[] { "netstandard" }),
                "Core must reference nothing but the netstandard facade.");
        }

        [Test]
        public void Core_supplies_the_IsExternalInit_shim()
        {
            // netstandard2.1 does not ship IsExternalInit, so 'init' accessors
            // and positional records do not compile without this shim. It is
            // internal, hence the reflection rather than a direct reference.
            var shim = CoreAssembly.GetType(
                "System.Runtime.CompilerServices.IsExternalInit",
                throwOnError: false);

            Assert.That(
                shim,
                Is.Not.Null,
                "Core must declare the IsExternalInit shim or records and 'init' "
                + "accessors will not compile inside Core.");
        }
    }
}
