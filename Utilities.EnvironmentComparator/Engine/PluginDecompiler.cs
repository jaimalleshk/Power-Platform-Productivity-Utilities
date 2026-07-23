using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Utilities.EnvironmentComparator.Engine
{
    public class PluginDecompiler
    {
        /// <summary>
        /// Decompiles a Base64 encoded Plug-in Assembly DLL downloaded from D365 Web API (pluginassembly.content).
        /// Uses the ILSpy Decompiler Engine (ICSharpCode.Decompiler) to generate full C# source code for line-by-line diffing.
        /// </summary>
        public string DecompileBase64Assembly(string base64Content, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(base64Content))
            {
                return $"// Plug-in Assembly '{assemblyName}' content unavailable or empty.";
            }

            try
            {
                byte[] dllBytes = Convert.FromBase64String(base64Content);
                return DecompileAssemblyBytes(dllBytes, assemblyName);
            }
            catch (Exception ex)
            {
                return $"// Error decoding Base64 plug-in assembly binary: {ex.Message}";
            }
        }

        public string DecompileAssemblyBytes(byte[] dllBytes, string assemblyName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// Decompiled C# Source Code for Plug-in Assembly: {assemblyName}");
            sb.AppendLine($"// Binary Size: {dllBytes.Length} bytes");
            sb.AppendLine("// Decompiled via ILSpy Engine (ICSharpCode.Decompiler)");
            sb.AppendLine();

            string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{assemblyName}.dll");

            try
            {
                File.WriteAllBytes(tempFile, dllBytes);

                // Use ILSpy CSharpDecompiler
                var settings = new DecompilerSettings
                {
                    ThrowOnAssemblyResolveErrors = false,
                    ShowXmlDocumentation = true,
                    DecompileMemberBodies = true
                };

                var decompiler = new CSharpDecompiler(tempFile, settings);
                string decompiledCode = decompiler.DecompileWholeModuleAsString();

                sb.AppendLine(decompiledCode);
                return sb.ToString();
            }
            catch
            {
                // Fallback to Reflection inspection if CSharpDecompiler encounters an unresolvable reference
                return FallbackReflectionDecompile(dllBytes, assemblyName, sb);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }

        private string FallbackReflectionDecompile(byte[] dllBytes, string assemblyName, StringBuilder sb)
        {
            try
            {
                var asm = Assembly.Load(dllBytes);
                sb.AppendLine($"namespace {asm.GetName().Name ?? assemblyName}");
                sb.AppendLine("{");

                foreach (var type in asm.GetTypes())
                {
                    if (!type.IsClass) continue;

                    sb.AppendLine($"    // Class: {type.FullName}");
                    sb.AppendLine($"    public class {type.Name}");
                    sb.AppendLine("    {");

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        var parameters = string.Join(", ", System.Linq.Enumerable.Select(method.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}"));
                        sb.AppendLine($"        public {method.ReturnType.Name} {method.Name}({parameters})");
                        sb.AppendLine("        {");
                        sb.AppendLine("            // Method body IL decompilation via reflection fallback");
                        sb.AppendLine("        }");
                    }

                    sb.AppendLine("    }");
                }

                sb.AppendLine("}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"// Fallback reflection failed: {ex.Message}");
                return sb.ToString();
            }
        }
    }
}
