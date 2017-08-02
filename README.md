# Assembly Conflict Analyzer
This is a small console app that will print out any assembly version conflicts present from a given .NET binary.  This is useful for debugging assembly load failures or validating assembly binding redirects.

    > AssemblyConflicts.exe
    Find version conflicts for assemblies referenced by a given appication
    Usage:
            -a, --assembly[optional]... The root application assembly. E.g. 'C:\project\website.dll','Application.exe'

            -?, --help[optional]... Print this help message

            -s, --include-system[optional]... Include mscorlib, System, and System.* assemblies

## Example

Assume that you have a website named MyProject, and it requires multiple versions of a given assembly at runtime. You may notice that
you are getting runtime errors about failing to load a specific version of an assembly. Instead of waiting for users to report exceptions
or using the fusion logging facilities at runtime, you may want to statically check for conflicts.  This project allows you to do that.

After finding conflicts you can be deal with them by ensuring that assembly binding redirects exist, adding things to the GAC, or simply updating dependencies
so that MyProject and its dependencies all agree on what version of libraries they require.


    > AssemblyConflicts.exe -a C:\code\MyProject\web\bin\MyProject.dll

    log4net has conflicts. Reference paths
        (1.2.13.0, 669e0ddf0bb1aa2a) MyProject => Web.Common => MyProject.Common => log4net
        (2.0.8.0, 669e0ddf0bb1aa2a) MyProject => Web.Common => log4net
        (2.0.8.0, 669e0ddf0bb1aa2a) MyProject => log4net

    NHibernate has conflicts. Reference paths
        (4.0.0.4000, aa95f207798dfdb4) MyProject => Domain.Persistence => Model => NHibernate
        (4.1.0.4000, aa95f207798dfdb4) MyProject => Domain.Persistence => NHibernate
        (4.1.0.4000, aa95f207798dfdb4) MyProject => Domain.Actors => NHibernate
        (4.1.0.4000, aa95f207798dfdb4) MyProject => MyProjectBindings => MyProjectControllers => NHibernate


This output indicates that version 4.2.0.0 and 4.0.0.0 of an assembly named AntiXssLibrary are referenced.  MyProject references a project called Web.Common
which references AntiXssLibrary version 4.0.0.0.  However, MyProject also references another library called RusticiSoftware.ScormEngine which references
AntiXssLibrary version 4.2.0.0.  A similar case exists for NHibernate.