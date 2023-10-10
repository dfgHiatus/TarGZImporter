using Elements.Core;
using Elements.Assets;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TarGZImporter.Extractor;

namespace TarGZImporter;

public class TarGZImporter : ResoniteMod
{
    public override string Name => "TarGZImporter";
    public override string Author => "dfgHiatus, eia485";
    public override string Version => "2.0.0";
    public override string Link => "https://github.com/dfgHiatus/ResoniteUnityPackagesImporter";

    internal const string TAR_GZ_FILE_EXTENSION = ".tar.gz";
    private static ModConfiguration Config;
    private readonly static string CachePath = Path.Combine(Engine.Current.CachePath, "Cache", "DecompressedTarGZs");

    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importAsRawFiles = 
        new("importAsRawFiles",
        "Import files as raw binaries. TarGZ files can be very large, keep this true unless you know what you're doing!",
        () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importText = 
        new("importText", "Import Text", () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importTexture = 
        new("importTexture", "Import Textures", () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importDocument = 
        new("importDocument", "Import Documents", () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importMesh = 
        new("importMesh", "Import Meshes", () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importPointCloud =
        new("importPointCloud", "Import Point Clouds", () => true);
    [AutoRegisterConfigKey]
    private readonly static ModConfigurationKey<bool> importAudio = 
        new("importAudio", "Import Audio", () => true);
    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> importFont = 
        new("importFont", "Import Fonts", () => true);
    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> importVideo = 
        new("importVideo", "Import Videos", () => true);

    public override void OnEngineInit()
    {
        new Harmony("net.dfgHiatus.TarGZImporter").PatchAll();
        Config = GetConfiguration();
        Directory.CreateDirectory(CachePath);
    }
    
    public static string[] DecomposeTarGZs(string[] files)
    {
        var fileToHash = files.ToDictionary(file => file, GenerateMD5);
        HashSet<string> dirsToImport = new();
        HashSet<string> targzsToDecompress = new();
        foreach (var element in fileToHash)
        {
            var dir = Path.Combine(CachePath, element.Value);
            if (!Directory.Exists(dir))
                targzsToDecompress.Add(element.Key);
            else
                dirsToImport.Add(dir);
        }
        foreach (var package in targzsToDecompress)
        {
            var modelName = Path.GetFileNameWithoutExtension(package);
            if (ContainsUnicodeCharacter(modelName))
            {
                Error("TarGZ cannot have unicode characters in its file name.");
                continue;
            }
            var extractedPath = Path.Combine(CachePath, fileToHash[package]);
            TarGZExtractor.Unpack(package, extractedPath);
            dirsToImport.Add(extractedPath);
        }
        return dirsToImport.ToArray();
    }


    [HarmonyPatch(typeof(UniversalImporter), "Import", typeof(AssetClass), typeof(IEnumerable<string>),
        typeof(World), typeof(float3), typeof(floatQ), typeof(bool))]
    public class UniversalImporterPatch
    {
        static bool Prefix(ref IEnumerable<string> files)
        {
            List<string> hasTarGZ = new();
            List<string> notTarGZ = new();
            foreach (var file in files)
            {
                if (Path.GetFileName(file).ToLower() == TAR_GZ_FILE_EXTENSION)
                    hasTarGZ.Add(file);
                else
                    notTarGZ.Add(file);
            }
            
            List<string> allDirectoriesToBatchImport = new();
            foreach (var dir in DecomposeTarGZs(hasTarGZ.ToArray()))
                allDirectoriesToBatchImport.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(ShouldImportFile).ToArray());

            var slot = Engine.Current.WorldManager.FocusedWorld.AddSlot("TarGZ import");
            slot.PositionInFrontOfUser();
            BatchFolderImporter.BatchImport(slot, allDirectoriesToBatchImport, Config.GetValue(importAsRawFiles));

            if (notTarGZ.Count <= 0) return false;
            files = notTarGZ.ToArray();
            return true;
        }
    }
    
    private static bool ShouldImportFile(string file)
    {
        var extension = Path.GetExtension(file).ToLower();
        var assetClass = AssetHelper.ClassifyExtension(Path.GetExtension(file));
        return (Config.GetValue(importText) && assetClass == AssetClass.Text) 
            || (Config.GetValue(importTexture) && assetClass == AssetClass.Texture) 
            || (Config.GetValue(importDocument) && assetClass == AssetClass.Document) 
            || (Config.GetValue(importPointCloud) && assetClass == AssetClass.PointCloud) 
            || (Config.GetValue(importAudio) && assetClass == AssetClass.Audio) 
            || (Config.GetValue(importFont) && assetClass == AssetClass.Font) 
            || (Config.GetValue(importVideo) && assetClass == AssetClass.Video) 
            || (Config.GetValue(importMesh) && assetClass == AssetClass.Model && extension != ".xml")   // Handle an edge case where assimp will try to import .xml files as 3D models
            || Path.GetFileName(file).ToLower().EndsWith(TAR_GZ_FILE_EXTENSION);                                    // Handle recursive tar.gz imports
    }
    
    private static bool ContainsUnicodeCharacter(string input)
    {
        const int MaxAnsiCode = 255;
        return input.Any(c => c > MaxAnsiCode);
    }

    // Credit to delta for this method https://github.com/XDelta/
    private static string GenerateMD5(string filepath)
    {
        using var hasher = MD5.Create();
        using var stream = File.OpenRead(filepath);
        var hash = hasher.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "");
    }
}
