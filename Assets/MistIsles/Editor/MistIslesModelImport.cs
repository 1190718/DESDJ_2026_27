using UnityEditor;

namespace MistIsles.EditorTools
{
    /// <summary>
    /// Import settings for the creature FBX files exported from Blender
    /// (Tools/Blender/build_crab.py): Generic rig, axis conversion baked, and one clip per
    /// Blender action with a short name ("MistCrab|Walk" → "Walk"). Idle and Walk loop.
    /// </summary>
    public class MistIslesModelImport : AssetPostprocessor
    {
        private const string CreatureFolder = "Assets/MistIsles/Art/Creatures/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CreatureFolder))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.bakeAxisConversion = true;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CreatureFolder))
                return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                string shortName = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
                clip.name = shortName;
                clip.loopTime = shortName == "Idle" || shortName == "Walk";
            }
            importer.clipAnimations = clips;
        }
    }
}
