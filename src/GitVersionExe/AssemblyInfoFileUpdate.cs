namespace GitVersion
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GitVersion.Helpers;
    using GitVersion.VersionAssemblyInfoResources;

    class AssemblyInfoFileUpdate : IDisposable
    {
        List<Action> restoreBackupTasks = new List<Action>();
        List<Action> cleanupBackupTasks = new List<Action>();
        
        public AssemblyInfoFileUpdate(Arguments args, string workingDirectory, VersionVariables variables, IFileSystem fileSystem)
        {
            if (!args.UpdateAssemblyInfo) return;

            if (args.Output != OutputType.Json)
                Console.WriteLine("Updating assembly info files");

            var assemblyInfoFiles = GetAssemblyInfoFiles(workingDirectory, args, fileSystem);

            foreach (var assemblyInfoFile in assemblyInfoFiles.Select(f => new FileInfo(f)))
            {
                var backupAssemblyInfo = assemblyInfoFile.FullName + ".bak";
                var localAssemblyInfo = assemblyInfoFile.FullName;
                fileSystem.Copy(assemblyInfoFile.FullName, backupAssemblyInfo, true);
                restoreBackupTasks.Add(() =>
                {
                    if (fileSystem.Exists(localAssemblyInfo))
                        fileSystem.Delete(localAssemblyInfo);
                    fileSystem.Move(backupAssemblyInfo, localAssemblyInfo);
                });
                cleanupBackupTasks.Add(() => fileSystem.Delete(backupAssemblyInfo));

                var assemblyVersion = variables.AssemblySemVer;
                var assemblyInfoVersion = variables.InformationalVersion;
                var assemblyFileVersion = variables.MajorMinorPatch + ".0";
                var fileContents = fileSystem.ReadAllText(assemblyInfoFile.FullName)
                    .RegexReplace(@"AssemblyVersion\(""[^""]*""\)", string.Format("AssemblyVersion(\"{0}\")", assemblyVersion))
                    .RegexReplace(@"AssemblyInformationalVersion\(""[^""]*""\)", string.Format("AssemblyInformationalVersion(\"{0}\")", assemblyInfoVersion))
                    .RegexReplace(@"AssemblyFileVersion\(""[^""]*""\)", string.Format("AssemblyFileVersion(\"{0}\")", assemblyFileVersion));

                fileSystem.WriteAllText(assemblyInfoFile.FullName, fileContents);
            }
        }

        static IEnumerable<string> GetAssemblyInfoFiles(string workingDirectory, Arguments args, IFileSystem fileSystem)
        {
            if (args.UpdateAssemblyInfoFileName != null && args.UpdateAssemblyInfoFileName.Any())
            {
                foreach (var item in args.UpdateAssemblyInfoFileName)
                {
                    var fullPath = Path.Combine(workingDirectory, item);

                    if (EnsureVersionAssemblyInfoFile(args, fileSystem, fullPath))
                    {
                        yield return fullPath;
                    }
                }
            }
            else
            {
                foreach (var item in fileSystem.DirectoryGetFiles(workingDirectory, "AssemblyInfo.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return item;
                }
            }
        }

        static bool EnsureVersionAssemblyInfoFile(Arguments arguments, IFileSystem fileSystem, string fullPath)
        {
            if (fileSystem.Exists(fullPath)) return true;

            if (!arguments.EnsureAssemblyInfo) return false;

            var assemblyInfoSource = AssemblyVersionInfoTemplates.GetAssemblyInfoTemplateFor(fullPath);
            if (!string.IsNullOrWhiteSpace(assemblyInfoSource))
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileSystem.DirectoryExists(fileInfo.Directory.FullName))
                {
                    fileSystem.CreateDirectory(fileInfo.Directory.FullName);
                }
                fileSystem.WriteAllText(fullPath, assemblyInfoSource);
                return true;
            }
            Logger.WriteWarning(string.Format("No version assembly info template available to create source file '{0}'", arguments.UpdateAssemblyInfoFileName));
            return false;
        }

        public void Dispose()
        {
            foreach (var restoreBackup in restoreBackupTasks)
            {
                restoreBackup();
            }

            cleanupBackupTasks.Clear();
            restoreBackupTasks.Clear();
        }

        public void DoNotRestoreAssemblyInfo()
        {
            foreach (var cleanupBackupTask in cleanupBackupTasks)
            {
                cleanupBackupTask();
            }
            cleanupBackupTasks.Clear();
            restoreBackupTasks.Clear();
        }
    }
}