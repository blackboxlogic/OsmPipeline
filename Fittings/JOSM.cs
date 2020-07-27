using System;
using System.IO;

namespace OsmPipeline.Fittings
{
	public static class JOSM
	{
		public static void MarkAsNeverUpload(string filePath)
		{
			var file = File.ReadAllText(filePath);
			if (file.IndexOf("upload=\"never\"", 0, 200) == -1)
			{
				file = file.Insert(file.IndexOf("<osm ") + "<osm ".Length, "upload=\"never\" ");
				File.WriteAllText(filePath, file);
			}
		}
	}
}
