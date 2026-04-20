using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;

namespace VRCADownloader.Editor
{
    public class VRCADownloaderWindow : EditorWindow
    {
        [MenuItem("Tools/VRCA Downloader")]
        private static void ShowWindow()
        {
            var window = GetWindow<VRCADownloaderWindow>();
            window.titleContent = new GUIContent("VRCA Downloader");
            window.minSize = new Vector2(400, 300);
        }

        private Vector2 scrollPosition;
        private string outputPath = "Assets/DownloadedVRCA";
        private bool autoLoadAfterDownload = false;
        private string searchFilter = "";
        private const string Version = "v1.0.3";

        private List<ApiAvatar> GetUploadedAvatars()
        {
            var field = typeof(VRCSdkControlPanel).GetField("uploadedAvatars",
                BindingFlags.Static | BindingFlags.NonPublic);
            return field != null ? (List<ApiAvatar>)field.GetValue(null) ?? new List<ApiAvatar>() : new List<ApiAvatar>();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            if (!APIUser.IsLoggedIn)
            {
                EditorGUILayout.HelpBox("请先在 VRChat SDK 中登录账号", MessageType.Warning);
                if (GUILayout.Button("打开 VRChat SDK"))
                    EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("保存路径:", GUILayout.Width(70));
            outputPath = EditorGUILayout.TextField(outputPath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("选择输出文件夹", "Assets", "");
                if (!string.IsNullOrEmpty(path)) outputPath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索:", GUILayout.Width(70));
            searchFilter = EditorGUILayout.TextField(searchFilter);
            if (GUILayout.Button("清除", GUILayout.Width(60)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            autoLoadAfterDownload = EditorGUILayout.Toggle("下载后自动加载到场景", autoLoadAfterDownload);

            EditorGUILayout.Space();

            if (GUILayout.Button("加载本地 VRCA 文件到场景"))
            {
                LoadVRCAFile();
            }

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            var uploadedAvatars = GetUploadedAvatars();
            
            if (!string.IsNullOrEmpty(searchFilter))
            {
                uploadedAvatars = uploadedAvatars.Where(avatar =>
                    avatar.name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    avatar.id.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();
            }

            if (uploadedAvatars.Count == 0)
            {
                if (string.IsNullOrEmpty(searchFilter))
                {
                    EditorGUILayout.HelpBox("请打开 VRChat SDK 中的 Content Manager 面板加载模型列表", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("没有找到匹配的模型", MessageType.Info);
                }
            }
            else
            {
                foreach (var avatar in uploadedAvatars)
                {
                    string outputFile = Path.Combine(outputPath, $"{avatar.id}.vrca");
                    bool fileExists = File.Exists(outputFile);

                    Color originalColor = GUI.backgroundColor;
                    if (fileExists)
                        GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);

                    EditorGUILayout.BeginHorizontal("box");

                    GUI.backgroundColor = originalColor;

                    Texture2D thumbnail = VRCSdkControlPanel.ImageCache?.ContainsKey(avatar.id) == true
                        ? VRCSdkControlPanel.ImageCache[avatar.id] : null;

                    GUILayout.Box(thumbnail ?? Texture2D.whiteTexture, GUILayout.Width(64), GUILayout.Height(64));

                    EditorGUILayout.BeginVertical();
                    GUILayout.Label(avatar.name, EditorStyles.boldLabel);
                    GUILayout.Label($"{avatar.id.Substring(0, Math.Min(12, avatar.id.Length))}...", EditorStyles.miniLabel);
                    if (fileExists)
                    {
                        GUIStyle downloadedStyle = new GUIStyle(EditorStyles.miniLabel);
                        downloadedStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
                        GUILayout.Label("已下载", downloadedStyle);
                    }
                    EditorGUILayout.EndVertical();

                    string buttonText = fileExists ? "重新下载" : "下载";
                    if (GUILayout.Button(buttonText, GUILayout.Width(80)))
                        DownloadAvatar(avatar, fileExists);

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUIStyle linkStyle = new GUIStyle(EditorStyles.label);
            linkStyle.normal.textColor = new Color(0.3f, 0.5f, 1f);
            linkStyle.hover.textColor = new Color(0.5f, 0.7f, 1f);
            linkStyle.fontSize = 10;
            
            string versionText = $"{Version} by PuddingKC";
            Rect linkRect = GUILayoutUtility.GetRect(new GUIContent(versionText), linkStyle);
            
            if (GUI.Button(linkRect, versionText, linkStyle))
            {
                Application.OpenURL("https://github.com/Null-K/VRChatVRCADownloader-Unity");
            }
            
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DownloadAvatar(ApiAvatar avatar, bool fileExists = false)
        {
            if (string.IsNullOrEmpty(avatar.assetUrl))
            {
                EditorUtility.DisplayDialog("下载错误", "无法获取下载链接，模型上传时间可能过早", "确定");
                return;
            }

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            string outputFile = Path.Combine(outputPath, $"{avatar.id}.vrca");

            if (fileExists)
            {
                if (!EditorUtility.DisplayDialog("文件已存在",
                    $"{avatar.name} 已下载\n\n{outputFile}\n\n是否重新下载？",
                    "重新下载", "取消"))
                {
                    return;
                }
            }

            EditorUtility.DisplayProgressBar("下载中", $"正在下载 {avatar.name}...", 0f);

            ApiFile.DownloadFile(
                avatar.assetUrl,
                bytes =>
                {
                    File.WriteAllBytes(outputFile, bytes);
                    EditorUtility.ClearProgressBar();

                    if (autoLoadAfterDownload)
                    {
                        LoadVRCAFileFromPath(outputFile);
                    }
                    else
                    {
                        if (EditorUtility.DisplayDialog("下载完成",
                            $"{outputFile}",
                            "打开文件夹", "确定"))
                        {
                            EditorUtility.RevealInFinder(outputFile);
                        }
                    }

                    Repaint();
                },
                error =>
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("下载失败", error, "确定");
                },
                (downloaded, total) =>
                {
                    EditorUtility.DisplayProgressBar("下载中",
                        $"{avatar.name} - {FormatBytes(downloaded)} / {FormatBytes(total)}",
                        (float)downloaded / total);
                }
            );
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void LoadVRCAFile()
        {
            string filePath = EditorUtility.OpenFilePanel("选择 VRCA 文件", outputPath, "vrca");
            if (string.IsNullOrEmpty(filePath))
                return;

            LoadVRCAFileFromPath(filePath);
        }

        private void LoadVRCAFileFromPath(string filePath)
        {
            try
            {
                AssetBundle.UnloadAllAssetBundles(false);
                AssetBundle ab = AssetBundle.LoadFromFile(filePath);

                if (ab == null)
                {
                    EditorUtility.DisplayDialog("加载失败", "无法加载 AssetBundle 文件", "确定");
                    return;
                }

                if (ab.isStreamedSceneAssetBundle)
                {
                    List<string> scenePaths = ab.GetAllScenePaths().ToList();
                    if (scenePaths.Count > 0)
                    {
                        SceneManager.LoadScene(Path.GetFileNameWithoutExtension(scenePaths[0]));
                        EditorUtility.DisplayDialog("加载成功", "场景已加载", "确定");
                    }
                }
                else
                {
                    List<UnityEngine.Object> loadedAssets = ab.LoadAllAssets().ToList();
                    int count = 0;
                    foreach (UnityEngine.Object asset in loadedAssets)
                    {
                        if (asset is GameObject)
                        {
                            UnityEngine.Object.Instantiate((GameObject)asset);
                            count++;
                        }
                    }
                    EditorUtility.DisplayDialog("加载成功", $"已加载 {count} 个对象到场景", "确定");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("加载失败", $"{e.Message}", "确定");
            }
        }
    }
}