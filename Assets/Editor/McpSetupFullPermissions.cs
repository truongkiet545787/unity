using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    [InitializeOnLoad]
    public static class McpSetupFullPermissions
    {
        static McpSetupFullPermissions()
        {
            EditorApplication.delayCall += ApplyFullPermissionsAndConnect;
        }

        [MenuItem("Tools/MCP/Grant Full Permissions and Connect")]
        public static void ApplyFullPermissionsAndConnect()
        {
            try
            {
                // 1. Core preferences & security
                EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
                EditorPrefs.SetBool("MCPForUnity.SetupCompleted", true);
                EditorPrefs.SetBool("MCPForUnity.CustomToolRegistrationEnabled", true);
                EditorPrefs.SetBool("MCPForUnity.HttpServerLaunchConfirmed", true);
                EditorPrefs.SetBool("MCPForUnity.Security.AllowLanHttpBind", true);
                EditorPrefs.SetBool("MCPForUnity.Security.AllowInsecureRemoteHttp", true);
                EditorPrefs.SetBool("MCPForUnity.ProjectScopedTools.LocalHttp", true);
                EditorPrefs.SetBool("MCPForUnity.DebugLogs", true);
                EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", false); // Use stdio by default
                EditorPrefs.SetBool("MCPForUnity.LockCursorConfig", true); // Prevent overwriting client config

                // 2. Enable all MCP tools (Full permissions)
                string[] tools = new[]
                {
                    "execute_code",
                    "execute_custom_tool",
                    "execute_menu_item",
                    "find_gameobjects",
                    "find_in_file",
                    "manage_animation",
                    "manage_asset",
                    "manage_build",
                    "manage_camera",
                    "manage_components",
                    "manage_editor",
                    "manage_gameobject",
                    "manage_graphics",
                    "manage_material",
                    "manage_packages",
                    "manage_physics",
                    "manage_prefabs",
                    "manage_probuilder",
                    "manage_profiler",
                    "manage_scene",
                    "manage_script",
                    "manage_scriptable_object",
                    "manage_shader",
                    "manage_texture",
                    "manage_tools",
                    "manage_ui",
                    "manage_vfx",
                    "read_console",
                    "refresh_unity",
                    "run_tests",
                    "script_apply_edits",
                    "unity_docs",
                    "unity_reflect",
                    "batch_execute",
                    "generate_audio",
                    "generate_image",
                    "generate_model",
                    "import_model",
                    "import_model_file",
                    "preflight"
                };

                foreach (var tool in tools)
                {
                    EditorPrefs.SetBool("MCPForUnity.ToolEnabled." + tool, true);
                }

                // 3. Start Stdio bridge host
                Type stdioHostType = Type.GetType("MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor");
                if (stdioHostType != null)
                {
                    MethodInfo startMethod = stdioHostType.GetMethod("StartAutoConnect", BindingFlags.Public | BindingFlags.Static);
                    if (startMethod != null)
                    {
                        startMethod.Invoke(null, null);
                    }
                }

                // 4. Configure Antigravity client if configurator exists
                Type antigravityConfigType = Type.GetType("MCPForUnity.Editor.Clients.Configurators.AntigravityConfigurator, MCPForUnity.Editor");
                if (antigravityConfigType != null)
                {
                    var instance = Activator.CreateInstance(antigravityConfigType);
                    MethodInfo configureMethod = antigravityConfigType.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance);
                    if (configureMethod != null)
                    {
                        configureMethod.Invoke(instance, null);
                    }
                }

                Debug.Log("[MCP for Unity] Full permissions granted and MCP server connection initialized!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MCP for Unity] Setup notice: " + ex.Message);
            }
        }
    }
}
