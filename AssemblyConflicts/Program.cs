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
                {
                    parser.ShowUsage();
                }

                if (!string.IsNullOrEmpty(_arguments.ApplicationAssembly))
                {
                    PrintConflicts(_arguments.ApplicationAssembly);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return 1;
            }
            return 0;
        }

        private static void PrintConflicts(string fileName)
        {
            var fullPath = Path.GetFullPath(fileName);
            var rootAssembly = AssemblyDefinition.ReadAssembly(fullPath);

            _resolver = new AssemblyResolver();
            _resolver.AddSearchDirectory(fullPath);
            _resolver.RegisterAssembly(rootAssembly);

            var allReferencedAssemblies = new List<AssemblyNameReference>();
            var rootGraph = GetAllReferences(
                rootAssembly,
                allReferencedAssemblies,
                null,
                new Dictionary<string, ReferenceGraph>());

            var referencesGroupedByName = allReferencedAssemblies.GroupBy(n => n.Name);
            foreach (var assemblyReferences in referencesGroupedByName)
            {
                if (assemblyReferences.Count() > 1)
                {
                    Console.WriteLine("{0} has conflicts. Reference paths", assemblyReferences.Key);
                    foreach (var reference in CalculateReferencePaths(assemblyReferences.Key, rootGraph))
                    {
                        string path = reference.Item1;
                        Version version = reference.Item2;
                        string publicKeyToken = reference.Item3;

                        Console.WriteLine("  ({0}, {2}) {1}", version, path, publicKeyToken);
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
                    try
                    {
                        var referencedAssembly = _resolver.Resolve(referencedAssemblyName);
                        var mappedAssemblyName = referencedAssembly.Name;
                        if (memoized.ContainsKey(mappedAssemblyName.FullName))
                            childGraph.References.Add(memoized[mappedAssemblyName.FullName]);
                        else
                            GetAllReferences(referencedAssembly, allReferencedAssemblies, childGraph, memoized);
                    }
                    catch (AssemblyResolutionException)
                    {
                        var referenceGraph = new ReferenceGraph { AssemblyName = referencedAssemblyName };
                        memoized.Add(referencedAssemblyName.FullName, referenceGraph);
                        childGraph.References.Add(referenceGraph);
                        allReferencedAssemblies.Add(referencedAssemblyName);
                    }
                }
            }
            return childGraph;
        }

        //private static ModuleDefinition LoadAssembly(AssemblyNameReference assemblyName)
        //{
        //    if (_loadedModuleDefinitions.ContainsKey(assemblyName.FullName))
        //        return _loadedModuleDefinitions[assemblyName.FullName];

        //    var assemblyInfo = new AssemblyInfo
        //    {
        //        currentAssemblyPathSize = 255,
        //        currentAssemblyPath = new String('\0', 255)
        //    };
        //    var hResult = _gac.QueryAssemblyInfo(QueryTypeId.None, GetFusionCompatibleFullName(assemblyName), ref assemblyInfo);
        //    if (!HResult.IsSuccess(hResult))
        //        throw new AssemblyResolutionException();
        //    var module = ModuleDefinition.ReadModule(assemblyInfo.currentAssemblyPath);
        //    _loadedModuleDefinitions[module.Assembly.FullName] = module;
        //    return module;
        //}

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
            CalculateReferencePaths(assemblySimpleName,
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