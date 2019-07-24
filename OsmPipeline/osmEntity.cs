using System;
using System.Xml.Serialization;

namespace OsmPipeline
{
	//public class osmChange

	public class osmEntity
	{
		[XmlElement]
		public string note;
		[XmlElement]
		public osmMeta meta;
		[XmlElement]
		public osmBounds bounds;
		[XmlElement]
		public osmNode[] node;
		[XmlElement]
		public osmWay[] way;
		[XmlElement]
		public osmRelation[] relation;
		[XmlAttribute]
		public decimal version;
		[XmlAttribute]
		public string generator;
	}

	public class osmMeta
	{
		[XmlAttribute("osm_base")]
		public string osmbase;
		[XmlAttribute]
		public string areas;
	}

	public class osmBounds
	{
		[XmlAttribute]
		public decimal minlat;
		[XmlAttribute]
		public decimal minlon;
		[XmlAttribute]
		public decimal maxlat;
		[XmlAttribute]
		public decimal maxlon;
	}

	public class osmNode : element
	{
		[XmlAttribute]
		public decimal lat;
		[XmlAttribute]
		public decimal lon;
	}

	public class osmWay : element
	{
		[XmlElement]
		public osmWayND[] nd;
	}

	public class osmWayND
	{
		[XmlAttribute]
		public ulong @ref;
	}

	public class osmRelation : element
	{
		[XmlElement]
		public osmRelationMember[] member;
	}

	public class osmRelationMember
	{
		[XmlAttribute]
		public string type; // either: node/way
		[XmlAttribute]
		public ulong @ref;
		[XmlAttribute]
		public string role;
	}

	public abstract class element
	{
		[XmlElement]
		public osmTag[] tag { get; set; }
		[XmlAttribute]
		public long id;
		// Meta:
		[XmlAttribute]
		public uint version;
		[XmlAttribute]
		public DateTime timestamp;
		[XmlAttribute]
		public ulong changeset;
		[XmlAttribute]
		public string user;
		[XmlAttribute]
		public ulong uid;
		[XmlAttribute]
		public bool visible;
	}

	public class osmTag
	{
		[XmlAttribute]
		public string k;
		[XmlAttribute]
		public string v;
	}
}
