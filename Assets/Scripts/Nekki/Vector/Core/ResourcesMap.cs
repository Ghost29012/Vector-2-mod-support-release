using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using Nekki.Vector.Core.User;
using UnityEngine;
using UnityEngine.U2D;

namespace Nekki.Vector.Core
{
	public static class ResourcesMap
	{
		private static Dictionary<string, Sprite[]> _AtlasCache = new Dictionary<string, Sprite[]>();

		private static Dictionary<string, Sprite> _CustomSpriteCache = new Dictionary<string, Sprite>();

		private static string QualityPrefix
		{
			get
			{
				return (!DataLocal.Current.Settings.UseLowResGraphics) ? string.Empty : "_low";
			}
		}

		public static Mesh GetMesh(string p_name)
		{
			string path = "Meshes/" + p_name;
			return (Mesh)Resources.Load(path, typeof(Mesh));
		}

		public static Sprite GetSprite(string p_name)
		{
			string[] array = p_name.Split('.');
			string text = array[0];
			string qualityPrefix = QualityPrefix;
			if (array.Length == 1)
			{
				Sprite sprite2 = ResourcesAndBundles.Load<Sprite>(string.Format("Run/SingleSprite/{0}{1}", text, QualityPrefix));
				if (sprite2 != null)
				{
					return sprite2;
				}
				return GetCustomSprite(p_name);
			}
			Sprite[] orCreateAtlas = GetOrCreateAtlas(text);
			for (int i = 0; i < orCreateAtlas.Length; i++)
			{
				if (orCreateAtlas[i].name == p_name)
				{
					return orCreateAtlas[i];
				}
			}
			return GetCustomSprite(p_name);
		}

		public static List<Sprite> GetFramesSequence(string p_name)
		{
			string[] array = p_name.Split('.');
			string p_atlasName = array[0];
			Sprite[] orCreateAtlas = GetOrCreateAtlas(p_atlasName);
			List<Sprite> list = new List<Sprite>();
			for (int i = 0; i < orCreateAtlas.Length; i++)
			{
				if (orCreateAtlas[i].name.IndexOf(p_name) != -1)
				{
					list.Add(orCreateAtlas[i]);
				}
			}
			list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
			return list;
		}

		public static List<KeyValuePair<Sprite, int>> GetCustomFramesSequence(string p_name)
		{
			List<KeyValuePair<Sprite, int>> list = new List<KeyValuePair<Sprite, int>>();
			XmlDocument xmlDocument = XmlUtils.OpenXMLDocument(VectorPaths.CustomAnimations + "/" + p_name, string.Empty);
			XmlNode xmlNode = xmlDocument["CustomAnimation"];
			string[] array = xmlNode.Attributes["Atlas"].Value.Split('.');
			string text = array[0];
			Sprite[] orCreateAtlas = GetOrCreateAtlas(text);
			foreach (XmlNode childNode in xmlNode.ChildNodes)
			{
				Sprite key = null;
				for (int i = 0; i < orCreateAtlas.Length; i++)
				{
					if (orCreateAtlas[i].name == text + "." + childNode.Attributes["Name"].Value)
					{
						key = orCreateAtlas[i];
						break;
					}
				}
				KeyValuePair<Sprite, int> item = new KeyValuePair<Sprite, int>(key, Convert.ToInt32(childNode.Attributes["Frames"].Value));
				list.Add(item);
			}
			return list;
		}

		public static void ResetSpriteAtlasCache()
		{
			_AtlasCache.Clear();
			_CustomSpriteCache.Clear();
		}

		private static Sprite GetCustomSprite(string p_name)
		{
			if (string.IsNullOrEmpty(p_name))
			{
				return null;
			}
			Sprite value;
			if (_CustomSpriteCache.TryGetValue(p_name, out value))
			{
				return value;
			}
			string storageRoot = !string.IsNullOrEmpty(VectorPaths.CurrentStorage) ? VectorPaths.CurrentStorage : Application.persistentDataPath;
			string customRoot = Path.Combine(storageRoot, "custom_textures");
			if (!Directory.Exists(customRoot))
			{
				Directory.CreateDirectory(customRoot);
				return null;
			}
			string[] candidates = BuildCustomTextureCandidates(p_name);
			for (int i = 0; i < candidates.Length; i++)
			{
				string path = Path.Combine(customRoot, candidates[i]);
				if (!File.Exists(path))
				{
					continue;
				}
				Texture2D texture = ResourceManager.GetTextureFromExternal(path);
				if (texture == null)
				{
					continue;
				}
				texture.wrapMode = TextureWrapMode.Clamp;
				texture.filterMode = FilterMode.Point;
				// Match editor placement anchor (top-left) to avoid runtime position offsets.
				Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0f, 1f), 1f);
				sprite.name = p_name;
				_CustomSpriteCache[p_name] = sprite;
				return sprite;
			}
			return null;
		}

		private static string[] BuildCustomTextureCandidates(string p_name)
		{
			string fileName = Path.GetFileName(p_name);
			string stem = Path.GetFileNameWithoutExtension(fileName);
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(fileName))
			{
				list.Add(fileName + ".png");
				list.Add(fileName + ".jpg");
				list.Add(fileName + ".jpeg");
				list.Add(fileName + ".bmp");
				list.Add(fileName);
			}
			if (!string.IsNullOrEmpty(stem) && stem != fileName)
			{
				list.Add(stem + ".png");
				list.Add(stem + ".jpg");
				list.Add(stem + ".jpeg");
				list.Add(stem + ".bmp");
			}
			int dotIndex = p_name.IndexOf('.');
			if (dotIndex > 0)
			{
				string firstSegment = p_name.Substring(0, dotIndex);
				list.Add(firstSegment + ".png");
				list.Add(firstSegment + ".jpg");
				list.Add(firstSegment + ".jpeg");
				list.Add(firstSegment + ".bmp");
			}
			return list.Distinct().ToArray();
		}

		private static Sprite[] GetOrCreateAtlas(string p_atlasName)
		{
			Sprite[] value = null;
			if (!_AtlasCache.TryGetValue(p_atlasName, out value))
			{
				SpriteAtlas atlas = ResourcesAndBundles.Load<SpriteAtlas>(string.Format("Run/Atlases/{0}{1}", p_atlasName, QualityPrefix));
				if (atlas == null)
				{
					value = new Sprite[0];
					_AtlasCache.Add(p_atlasName, value);
					return value;
				}
				value = new Sprite[atlas.spriteCount];
				atlas.GetSprites(value);
				foreach (Sprite sprite in value)
				{
					sprite.name = sprite.name.Replace("(Clone)", "");
				}
				_AtlasCache.Add(p_atlasName, value);
			}
			return value;
		}
	}
}
