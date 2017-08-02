using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLineParser.Arguments;
using CommandLineParser.Exceptions;
using Mono.Cecil;

namespace AssemblyConflicts
{
    public class Arguments
    {
        [ValueArgument(
            typeof(string),
            'a',
            "assembly",
            Description = "The root application assembly. E.g. 'C:\\project\\website.dll','Application.exe'")]
        public string ApplicationAssembly { get; set; }

        [SwitchArgument('?', "help", false, Description = "Print this help message")]
        public bool Help { get; set; }

        [SwitchArgument('s', "include-system", false, Description = "Include mscorlib, System, and System.* assemblies")]
        public bool IncludeSystem { get; set; }
    }

    public class Program
    {
        private static Arguments _arguments;
        private static AssemblyResolver _resolver;

        private static int Main(string[] args)
        {
            CommandLineParser.CommandLineParser parser = new CommandLineParser.CommandLineParser
            {
                ShowUsageHeader = "Find version conflicts for assemblies referenced by a given appication"
            };

            _arguments = new Arguments();
            parser.ExtractArgumentAttributes(_arguments);

            try
            {
                parser.ParseCommandLine(args);
                if (args.Length == 0)
                {
                    parser.ShowUsage();
                    return 1;
                }
            }
            catch (CommandLineException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                parser.ShowUsage();
                return 1;
            }

            try
            {
                if (_arguments.Help)
                    parser.ShowUsage();

                if (!string.IsNullOrEmpty(_arguments.ApplicationAssembly))
                    PrintConflicts();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return 1;
            }
            return 0;
        }

        private static void PrintConflicts()
        {
            var fullPath = Path.GetFullPath(_arguments.ApplicationAssembly);
            var rootAssembly = AssemblyDefinition.ReadAssembly(fullPath);

            _resolver = new AssemblyResolver();
            _resolver.AddSearchDirectory(Path.GetDirectoryName(fullPath));
            _resolver.RegisterAssembly(rootAssembly);

            var allReferencedAssemblies = new List<AssemblyNameReference>();
            var rootGraph = GetAllReferences(
                rootAssembly,
                allReferencedAssemblies,
                null,
                new Dictionary<string, ReferenceGraph>());

            var references = allReferencedAssemblies;
            if (!_arguments.IncludeSystem)
            {
                references = references
                    .Where(r => r.Name != "mscorlib" &&
                                r.Name != "System" &&
                                !r.Name.StartsWith("System."))
                    .OrderBy(r => r.FullName)
                    .ToList();
            }
            foreach (var assemblyReferences in references.GroupBy(n => n.Name))
            {
                if (assemblyReferences.Count() > 1)
                {
                    Console.WriteLine("{0} has conflicts. Reference paths", assemblyReferences.Key);
                    var orderedReferencePaths = CalculateReferencePaths(assemblyReferences.Key, rootGraph)
                        .OrderBy(p => p.version)
                        .ThenBy(p => p.publicKeyToken);
                    foreach (var reference in orderedReferencePaths)
                    {
                        Console.WriteLine("  ({0}, {2}) {1}",
                            reference.version,
                            reference.path,
                            reference.publicKeyToken);
                    }
                    Console.WriteLine();
                }
            }
        }

        private static ReferenceGraph GetAllReferences(
            AssemblyDefinition assembly,
            List<AssemblyNameReference> allReferencedAssemblies,
            ReferenceGraph parentGraph,
            Dictionary<string, ReferenceGraph> memoized)
        {
            var assemblyName = assembly.Name;
            allReferencedAssemblies.Add(assemblyName);
            var childGraph = new ReferenceGraph { AssemblyName = assemblyName };
            parentGraph?.References.Add(childGraph);

            memoized.Add(assemblyName.FullName, childGraph);
            foreach (var referencedAssemblyName in assembly.Modules.SelectMany(m => m.AssemblyReferences))
            {
                if (memoized.ContainsKey(referencedAssemblyName.FullName))
                {
                    childGraph.References.Add(memoized[referencedAssemblyName.FullName]);
                }
                else
                {
                    AssemblyDefinition asm = null;
                    try
                    {
                        asm = _resolver.Resolve(referencedAssemblyName);
                    }
                    catch (AssemblyResolutionException) { }

                    if (asm == null || asm.FullName != referencedAssemblyName.FullName)
                    {
                        var referenceGraph = new ReferenceGraph { AssemblyName = referencedAssemblyName };
                        memoized.Add(referencedAssemblyName.FullName, referenceGraph);
                        childGraph.References.Add(referenceGraph);
                        allReferencedAssemblies.Add(referencedAssemblyName);
                    }
                    else if (memoized.ContainsKey(asm.Name.FullName))
                    {
                        childGraph.References.Add(memoized[asm.Name.FullName]);
                    }
                    else
                    {
                        GetAllReferences(asm, allReferencedAssemblies, childGraph, memoized);
                    }
                }
            }
            return childGraph;
        }

        public static string GetFusionCompatibleFullName(AssemblyNameReference assemblyName)
        {
            return assemblyName.Name
                   + (assemblyName.Version == null ? "" : ", Version=" + assemblyName.Version)
                   + (string.IsNullOrEmpty(assemblyName.Culture) ? "" : ", Culture=" + assemblyName.Culture);
        }

        private static List<(string path, Version version, string publicKeyToken)> CalculateReferencePaths(
            string assemblySimpleName,
            ReferenceGraph graphNode)
        {
            var result = new List<(string path, Version version, string publicKeyToken)>();
            CalculateReferencePaths(
                assemblySimpleName,
                graphNode,
                graphNode.AssemblyName.Name,
                new List<ReferenceGraph>(),
                result);
            return result;
        }

        private static void CalculateReferencePaths(
            string assemblySimpleName,
            ReferenceGraph graphNode,
            string path,
            List<ReferenceGraph> visited,
            List<(string path, Version version, string publicKeyToken)> result)
        {
            foreach (var reference in graphNode.References)
            {
                string referencePath = path + " => " + reference.AssemblyName.Name;
                if (reference.AssemblyName.Name == assemblySimpleName)
                {
                    var publicKeyToken = string.Join(null,
                        reference.AssemblyName.PublicKeyToken.Select(b => b.ToString("x2")));
                    result.Add((referencePath, reference.AssemblyName.Version, publicKeyToken));
                }
                else
                {
                    if (!visited.Contains(reference))
                    {
                        visited.Add(reference);
                        CalculateReferencePaths(
                            assemblySimpleName,
                            reference,
                            referencePath,
                            visited,
                            result);
                    }
                }
            }
        }
    }

    public class ReferenceGraph
    {
        public AssemblyNameReference AssemblyName;
        public List<ReferenceGraph> References = new List<ReferenceGraph>();

        public override string ToString()
        {
            return AssemblyName?.ToString() ?? "ReferenceGraph";
        }
    }

    public class AssemblyResolver : DefaultAssemblyResolver
    {
        public new void RegisterAssembly(AssemblyDefinition assembly)
        {
            base.RegisterAssembly(assembly);
        }
    }
}