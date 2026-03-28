using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Nekki.Vector.Core.Counter;
using Nekki.Vector.Core.Game;
using Nekki.Vector.Core.GameManagement;
using Nekki.Vector.Core.Generator.Test;
using UnityEngine;

namespace Nekki.Vector.Core.Generator
{
	public class LocationGenerator
	{
		public static int MaxAttemptCount = 5;

		private static int _GeneratedRoomId;

		private List<RoomData> _Rooms = new List<RoomData>();

		private List<RoomData> _SelectedRooms = new List<RoomData>();

		private List<string> _ObjectsFiles = new List<string>();

		public List<string> ObjectsFile
		{
			get
			{
				return _ObjectsFiles;
			}
		}

		public LocationGenerator(string p_file, NekkiRandom p_generator)
		{
			_GeneratedRoomId = 0;
			Parse(p_file);
		}

		public static string GetGeneratedRoomId()
		{
			string result = _GeneratedRoomId.ToString();
			_GeneratedRoomId++;
			return result;
		}

		private void Parse(string p_file)
		{
			XmlDocument xmlDocument = XmlUtils.OpenXMLDocument(p_file, string.Empty);
			foreach (XmlNode childNode in xmlDocument["Root"].ChildNodes)
			{
				if (!(childNode.Name != "Room"))
				{
					RoomData item = new RoomData(childNode);
					_Rooms.Add(item);
				}
			}
			ParseIncludes(xmlDocument["Root"]["Includes"]);
			LoadCustomRooms();
		}

		private void LoadCustomRooms()
		{
			// Use VectorPaths.CurrentStorage so the path matches where Vector stores all its data at runtime
			string storageRoot = !string.IsNullOrEmpty(VectorPaths.CurrentStorage)
				? VectorPaths.CurrentStorage
				: Application.persistentDataPath;
			string customRoot = Path.Combine(storageRoot, "custom_rooms");
			string customTexturesRoot = Path.Combine(storageRoot, "custom_textures");

			if (!Directory.Exists(customTexturesRoot))
			{
				Directory.CreateDirectory(customTexturesRoot);
			}

			if (!Directory.Exists(customRoot))
			{
				Directory.CreateDirectory(customRoot);
				Directory.CreateDirectory(Path.Combine(customRoot, "zone_2"));
				return;
			}

			// If a play command is active, only load the rooms that were explicitly requested.
			// This prevents old rooms in custom_rooms from sneaking into the pool when a
			// specific room is requested via the console/play command.
			bool isPlayCommand = GeneratorHelper.ToLoadRooms != null &&
			                     GeneratorHelper.ToLoadRooms.Count > 0;

			string[] files = Directory.GetFiles(customRoot, "*.xml", SearchOption.AllDirectories);
			foreach (string roomXmlPath in files)
			{
				if (roomXmlPath.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase))
					continue;

				string roomName = Path.GetFileNameWithoutExtension(roomXmlPath);

				// In play command mode, skip rooms that were not explicitly requested
				if (isPlayCommand && !GeneratorHelper.ToLoadRooms.Contains(roomName))
					continue;

				try
				{
					XmlDocument helperDoc = new XmlDocument();
					XmlElement roomNode = helperDoc.CreateElement("Room");
					roomNode.SetAttribute("Name", roomName);
					roomNode.SetAttribute("File", roomXmlPath);
					roomNode.SetAttribute("IncludeInPlayCommand", "0");

					// Check for a sidecar .meta.xml file for GeneratorLabels
					string sidecarPath = Path.ChangeExtension(roomXmlPath, ".meta.xml");
					if (File.Exists(sidecarPath))
					{
						try
						{
							XmlDocument sidecarDoc = new XmlDocument();
							sidecarDoc.Load(sidecarPath);
							XmlNode labelsNode = sidecarDoc["RoomMeta"]?["GeneratorLabels"];
							if (labelsNode != null)
							{
								XmlNode imported = helperDoc.ImportNode(labelsNode, true);
								roomNode.AppendChild(imported);
							}
							else
							{
								roomNode.AppendChild(helperDoc.CreateElement("GeneratorLabels"));
							}
						}
						catch
						{
							roomNode.AppendChild(helperDoc.CreateElement("GeneratorLabels"));
						}
					}
					else
					{
						roomNode.AppendChild(helperDoc.CreateElement("GeneratorLabels"));
					}

					helperDoc.AppendChild(roomNode);
					_Rooms.Add(new RoomData(roomNode));
					Debug.Log("[CustomRooms] Loaded: " + roomName + " from " + roomXmlPath);
				}
				catch (Exception e)
				{
					Debug.LogWarning("[CustomRooms] Failed to load " + roomXmlPath + ": " + e.Message);
				}
			}
		}

		private void ParseIncludes(XmlNode p_node)
		{
			foreach (XmlNode childNode in p_node.ChildNodes)
			{
				if (childNode.Name != "Library")
				{
					break;
				}
				_ObjectsFiles.Add(childNode.Attributes["Filename"].Value);
			}
		}

		public Room GetRoom(bool p_testMode = false)
		{
			Room randomRoom = GetRandomRoom();
			CounterController.Current.AddCountersToSelectedGeneratorLabels(randomRoom);
			if (p_testMode)
			{
				return randomRoom;
			}
			if (randomRoom == null)
			{
				DebugUtils.Dialog("Null room(bad generator conditions?)", true);
			}

			// Handle absolute paths for custom rooms — on Windows, Path.IsPathRooted()
			// correctly detects both C:\... and \\server\... style paths.
			XmlDocument xmlDocument;
			if (Path.IsPathRooted(randomRoom.File))
			{
				try
				{
					xmlDocument = new XmlDocument();
					xmlDocument.Load(randomRoom.File);
				}
				catch (Exception e)
				{
					DebugUtils.Dialog("Fatal: Error read XML (custom room: " + randomRoom.File + ")\n" + e.Message, true);
					return null;
				}
			}
			else
			{
				xmlDocument = XmlUtils.OpenXMLDocument(VectorPaths.Rooms, randomRoom.File);
			}

			if (xmlDocument["Root"]["Track"]["Properties"] != null)
			{
				randomRoom.CounterActions = CounterActions.Create(xmlDocument["Root"]["Track"]["Properties"]["CounterActions"], "ST_Default");
			}
			XmlNode newChild = xmlDocument["Root"]["Track"]["Content"];
			XmlNodeList xmlNodeList = xmlDocument.SelectNodes("//Selection[@Choice]");
			foreach (XmlNode item in xmlNodeList)
			{
				string text = XmlUtils.ParseString(item.Attributes["Parent"], string.Empty);
				if (text.Length > 0)
				{
					text += "/";
				}
				item.Attributes["Choice"].Value = randomRoom.UniqueName + "_" + text + item.Attributes["Choice"].Value;
			}
			XmlElement xmlElement = xmlDocument.CreateElement("Object");
			xmlElement.SetAttribute("Name", randomRoom.Name);
			xmlElement.SetAttribute("X", "0");
			xmlElement.SetAttribute("Y", "0");
			xmlElement.SetAttribute("Factor", "0");
			xmlElement.AppendChild(newChild);
			XmlNode xmlNode2 = xmlDocument.SelectSingleNode("Root/Track");
			xmlNode2.InsertBefore(xmlElement, xmlNode2.FirstChild);
			randomRoom.TmpNode = xmlDocument["Root"]["Track"]["Object"];
			return randomRoom;
		}

		private RoomData GetRoom(string p_name)
		{
			foreach (RoomData room in _Rooms)
			{
				if (room.Name == p_name)
				{
					return room;
				}
			}
			return null;
		}

		private Room GetRandomRoom()
		{
			return GetRandomRoom(_Rooms);
		}

		private Room GetRandomRoom(List<RoomData> p_rooms, int p_iteration = 0)
		{
			if (p_rooms.Count == 0 || (GeneratorTester.IsActive && GeneratorTester.IsIterationExpired))
			{
				return null;
			}
			CounterController.Current.CounterGenerationAttempt = p_iteration + 1;
			if (p_iteration == MaxAttemptCount)
			{
				DebugUtils.LogToConsole("The generator room fail on " + _SelectedRooms.Count + " room");
				VectorLog.GeneratorLog("FAIL !! ROOM NUMBER " + _SelectedRooms.Count);
				return null;
			}
			RoomConditionList conditionList = ZoneResource<RoomConditions>.Current.GetConditionList();
			if (Settings.WriteGeneratorLogs)
			{
				VectorLog.GeneratorLog(" ");
				VectorLog.GeneratorLog(conditionList);
			}
			Room randomRoom = GetRandomRoom(p_rooms, conditionList, out RoomData roomdata);
			if (randomRoom == null && (int)CounterController.Current.CounterEnableRoomReuse != 0 && p_rooms != _SelectedRooms)
			{
				if (Settings.WriteGeneratorLogs)
				{
					VectorLog.GeneratorLog(" ");
					VectorLog.GeneratorLog("The generator could not find room in list!");
					VectorLog.GeneratorLog("Try to generate from selected rooms list.");
				}
				DebugUtils.LogToConsole("The generator could not find room in list! Try to generate from selected rooms list.");
				randomRoom = GetRandomRoom(_SelectedRooms, p_iteration);
			}
			if (randomRoom == null)
			{
				if (Settings.WriteGeneratorLogs)
				{
					VectorLog.GeneratorLog(" ");
					VectorLog.GeneratorLog("The generator could not find room in list!");
					VectorLog.GeneratorLog("Try to generate againe");
				}
				DebugUtils.LogToConsole($"The generator could not find {roomdata.Name} in list! Try to generate againe.");
				return GetRandomRoom(p_rooms, p_iteration + 1);
			}
			return randomRoom;
		}

		private Room GetRandomRoom(List<RoomData> p_rooms, RoomConditionList p_conditions, out RoomData roomdata)
		{
			MainRandom.ShuffleList(p_rooms);
			Room room = null;
			roomdata = null;
			for (int i = 0; i < p_rooms.Count; i++)
			{
				RoomData roomData = p_rooms[i];
				roomdata = roomData;
				if (!roomData.Check() || !p_conditions.CheckRanges(roomData.Ranges))
				{
					continue;
				}
				room = roomData.CheckConditions(p_conditions);
				if (room == null)
				{
					continue;
				}
				if (p_rooms != _SelectedRooms)
				{
					p_rooms.Remove(roomData);
					if (!roomData.IsIncludeInPlayCommand)
					{
						roomData.ResetChoiceToIndefined();
						_SelectedRooms.Add(roomData);
					}
				}
				else
				{
					roomData.ResetChoiceToIndefined();
				}
				break;
			}
			return room;
		}
	}
}
