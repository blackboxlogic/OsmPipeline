using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SharpKml.Dom;
using SharpKml.Engine;

namespace OsmPipeline.Fittings
{
	public class KmlFileSource : ISource<Element>
	{
		private string FileName;

		public KmlFileSource(string fileName)
		{
			FileName = fileName;
		}

		public IEnumerator<Element> GetEnumerator()
		{
			using (var fileStream = File.Open(FileName, FileMode.Open))
			{
				var file = KmlFile.Load(fileStream);
				return file.Root.Flatten().GetEnumerator();
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			using (var fileStream = File.Open(FileName, FileMode.Open))
			{
				var file = KmlFile.Load(fileStream);
				return file.Root.Flatten().GetEnumerator();
			}
		}
	}
}
