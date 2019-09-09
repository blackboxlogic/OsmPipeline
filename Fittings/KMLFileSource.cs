using System;
using System.Collections.Generic;
using System.IO;
using SharpKml.Dom;
using SharpKml.Engine;

namespace OsmPipeline.Fittings
{
	public class KmlFileSource
	{
		public IEnumerator<Element> Get(string fileName)
		{
			using (var fileStream = File.Open(fileName, FileMode.Open))
			{
				var file = KmlFile.Load(fileStream);
				return file.Root.Flatten().GetEnumerator();
			}
		}
	}
}
