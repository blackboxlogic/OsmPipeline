using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Geo;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using static OsmPipeline.Fittings.Tags;
using ProjNet.CoordinateSystems;

namespace OsmPipeline.Fittings
{
	public static class Geometry
	{
		public static CompleteWay[] Reduce(CompleteWay[] ways)
		{
			return ways.Select(w => Reduce(w)).ToArray();
		}

		public static CompleteWay Reduce(CompleteWay way)
		{
			var lineString = way.AsLineString();
			var reducer = new NetTopologySuite.Precision.GeometryPrecisionReducer(new PrecisionModel() {  } );

			NetTopologySuite.Simplify.DouglasPeuckerLineSimplifier.Simplify(way.GetCoordinates().ToArray(), .0004);

			var geometry = (LineString)reducer.Reduce(lineString);
			//way = 
			return way;
		}

		public static LinearRing AsBoundingPolygon(CompleteRelation relation)
		{
			var thing = DefaultFeatureInterpreter.DefaultInterpreter.Interpret(relation).OfType<LinearRing>().First();
			relation.Members.Where(r => r.Role == "outer");
			return thing;
		}

		public static LineString AsLineString(this CompleteWay way)
		{
			var thing = DefaultFeatureInterpreter.DefaultInterpreter.Interpret(way);
			return null; // new LineString(way.GetCoordinates().ToArray());
		}

		public static CompleteWay AsCompleteWay(this LineString linestring)
		{
			//return new cmop
			//return new LineString(way.GetCoordinates().ToArray());
			return null;
		}

		public static string GetDistanceAndDirectionArrow(Coordinate from, Coordinate to)
		{
			return (int)DistanceMeters(from, to) + "m" + GetDirectionArrow(from, to);
		}

		public static char GetDirectionArrow(Coordinate from, Coordinate to)
		{
			var theta = Math.Atan2(to.Y - from.Y, to.X - from.X);
			var slice = (int)((theta + Math.PI) / (2 * Math.PI) * 16);
			var arrow = "←↙↙↓↓↘↘→→↗↗↑↑↖↖←"[slice];
			return arrow;
		}

		public static char ReverseArrow(char arrow)
		{
			return "→↗↑↖←↙↓↘"["←↙↓↘→↗↑↖".IndexOf(arrow)];
		}

		public static ICompleteOsmGeo AsComplete(this OsmGeo parent, IOsmGeoSource possibleChilden)
		{
			return parent.CreateComplete(possibleChilden);
		}

		// Dictionary is keys on: OsmGeo.Type.ToString() + element.Id
		public static ICompleteOsmGeo AsComplete(this OsmGeo parent, Dictionary<OsmGeoKey, OsmGeo> possibleChilden)
		{
			if (parent is ICompleteOsmGeo node) return node;
			else if (parent is Way way)
			{
				return AsComplete(way, possibleChilden);
			}
			else if (parent is Relation relation)
			{
				return AsComplete(relation, possibleChilden);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		// Dictionary is keys on: OsmGeo.Type.ToString() + element.Id
		public static ICompleteOsmGeo AsComplete(this OsmGeo parent, Dictionary<string, OsmGeo> possibleChilden)
		{
			if (parent is ICompleteOsmGeo node) return node;
			else if (parent is Way way)
			{
				return AsComplete(way, possibleChilden);
			}
			else if (parent is Relation relation)
			{
				return AsComplete(relation, possibleChilden);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		public static CompleteWay AsComplete(this Way way, Dictionary<string, OsmGeo> possibleChilden)
		{
			return new CompleteWay()
			{
				Id = way.Id.Value,
				Nodes = way.Nodes.Select(n => possibleChilden[OsmGeoType.Node.ToString() + n]).OfType<Node>().ToArray(),
				Tags = way.Tags,
				Version = way.Version,
				Visible = way.Visible,
				ChangeSetId = way.ChangeSetId,
				TimeStamp = way.TimeStamp,
				UserId = way.UserId,
				UserName = way.UserName
			};
		}

		public static CompleteWay AsComplete(this Way way, Dictionary<OsmGeoKey, OsmGeo> possibleChilden)
		{
			return new CompleteWay()
			{
				Id = way.Id.Value,
				Nodes = way.Nodes.Select(n => possibleChilden[new OsmGeoKey(OsmGeoType.Node, n)]).OfType<Node>().ToArray(),
				Tags = way.Tags,
				Version = way.Version,
				Visible = way.Visible,
				ChangeSetId = way.ChangeSetId,
				TimeStamp = way.TimeStamp,
				UserId = way.UserId,
				UserName = way.UserName
			};
		}

		public static CompleteRelation AsComplete(this Relation relation, Dictionary<string, OsmGeo> possibleChilden)
		{
			var members = relation.Members
						.Where(m => possibleChilden.ContainsKey(m.Type.ToString() + m.Id))
						.Select(m => new CompleteRelationMember()
						{
							Member = AsComplete(possibleChilden[m.Type.ToString() + m.Id], possibleChilden),
							Role = m.Role
						})
						.ToArray();
			return new CompleteRelation()
			{
				Id = relation.Id.Value,
				Members = members,
				Tags = relation.Tags,
				ChangeSetId = relation.ChangeSetId,
				TimeStamp = relation.TimeStamp,
				UserId = relation.UserId,
				UserName = relation.UserName,
				Version = relation.Version,
				Visible = relation.Visible
			};
		}

		public static CompleteRelation AsComplete(this Relation relation, Dictionary<OsmGeoKey, OsmGeo> possibleChilden)
		{
			var members = relation.Members
				.Where(m => possibleChilden.ContainsKey(new OsmGeoKey(m.Type, m.Id)))
				.Select(m => new CompleteRelationMember()
				{
					Member = AsComplete(possibleChilden[new OsmGeoKey(m.Type, m.Id)], possibleChilden),
					Role = m.Role
				})
				.ToArray();
			return new CompleteRelation()
			{
				Id = relation.Id.Value,
				Members = members,
				Tags = relation.Tags,
				ChangeSetId = relation.ChangeSetId,
				TimeStamp = relation.TimeStamp,
				UserId = relation.UserId,
				UserName = relation.UserName,
				Version = relation.Version,
				Visible = relation.Visible
			};
		}

		public static Way AsOsmGeo(this CompleteWay way)
		{
			return new Way()
			{
				Id = way.Id,
				Nodes = way.Nodes.Select(n => n.Id.Value).ToArray(),
				Tags = way.Tags,
				Version = way.Version,
				Visible = way.Visible,
				ChangeSetId = way.ChangeSetId,
				TimeStamp = way.TimeStamp,
				UserId = way.UserId,
				UserName = way.UserName
			};
		}

		// Playing with different ways to index elements, this one is the intent of OsmSharp
		public static IEnumerable<OsmGeo> WithChildren(this IEnumerable<OsmGeo> parents, IOsmGeoSource possibleChilden)
		{
			return parents.SelectMany(p => p.WithChildren(possibleChilden));
		}

		public static IEnumerable<OsmGeo> WithChildren(this IEnumerable<OsmGeo> parents, Dictionary<string, OsmGeo> possibleChilden)
		{
			return parents.SelectMany(p => p.WithChildren(possibleChilden));
		}

		public static IEnumerable<OsmGeo> WithChildren(this IEnumerable<OsmGeo> parents, Dictionary<OsmGeoKey, OsmGeo> possibleChilden)
		{
			return parents.SelectMany(p => p.WithChildren(possibleChilden));
		}

		public static IEnumerable<OsmGeo> WithChildren(this OsmGeo parent, Dictionary<string, OsmGeo> possibleChilden)
		{
			if (parent is Node) return new[] { parent };
			else if (parent is Way way)
			{
				return way.Nodes.Select(n => possibleChilden[OsmGeoType.Node.ToString() + n])
					.Append(parent);
			}
			else if (parent is Relation relation)
			{
				return relation.Members
					.SelectMany(m => possibleChilden.TryGetValue(m.Type.ToString() + m.Id, out OsmGeo child)
						? WithChildren(child, possibleChilden)
						: Enumerable.Empty<OsmGeo>())
					.Where(m => m != null)
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		public static IEnumerable<OsmGeo> WithChildren(this OsmGeo parent, Dictionary<OsmGeoKey, OsmGeo> possibleChilden)
		{
			if (parent is Node) return new[] { parent };
			else if (parent is Way way)
			{
				return way.Nodes.Select(n => possibleChilden[new OsmGeoKey(OsmGeoType.Node, n)])
					.Append(parent);
			}
			else if (parent is Relation relation)
			{
				return relation.Members
					.SelectMany(m => possibleChilden.TryGetValue(new OsmGeoKey(m.Type, m.Id), out OsmGeo child)
						? WithChildren(child, possibleChilden)
						: Enumerable.Empty<OsmGeo>())
					.Where(m => m != null)
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		// Needs to prevent infinite recusion using a reference id exclusion listS
		public static IEnumerable<OsmGeo> WithChildren(this OsmGeo parent, IOsmGeoSource possibleChilden)
		{
			if (parent is Node) return new[] { parent };
			else if (parent is Way way)
			{
				return way.Nodes.Select(n => possibleChilden.Get(OsmGeoType.Node, n))
					.Append(parent);
			}
			else if (parent is Relation relation)
			{
				return relation.Members
					.SelectMany(m =>
					{
						var child = possibleChilden.Get(m.Type, m.Id);
						return child != null
							? WithChildren(child, possibleChilden)
							: Enumerable.Empty<OsmGeo>();
					})
					.Where(m => m != null)
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		// Either IN a building, or distanceMeters from a building and no other plausible building within isolationMeters
		public static Dictionary<ICompleteOsmGeo, List<Node>> NodesInOrNearCompleteElements(
			ICompleteOsmGeo[] subjects, Node[] references, double distanceMeters, double isolationMeters,
			HashSet<long> ineligibleSubjectIds, int confidenceFactor = 4)
		{
			var inside = NodesInCompleteElements(subjects, references);
			var unmatchedNodes = references.Except(inside.SelectMany(r => r.Value)).ToArray();

			var edges = NodesNearCompleteElements(subjects, unmatchedNodes, isolationMeters);
			var byReference = edges.GroupBy(edge => edge.Reference)
				.ToDictionary(edgeGroup => edgeGroup.Key, edgeGroup => edgeGroup.OrderBy(g => g.Distance).ToArray());
			var bySubject = edges.GroupBy(edge => edge.Subject)
				.ToDictionary(edgeGroup => edgeGroup.Key, edgeGroup => edgeGroup.OrderBy(g => g.Distance).ToArray());
			var primaryPlausibleEdges = edges.Where(edge =>
				edge.Distance < distanceMeters
				// Is edge THE SOLE plausible connection from the reference
				&& (byReference[edge.Reference].Length == 1
					|| byReference[edge.Reference][1].Distance > edge.Distance * confidenceFactor)
				// Is edge a plausible from the subject
				&& edge.Distance < bySubject[edge.Subject][0].Distance * confidenceFactor);

			var near = primaryPlausibleEdges
				.GroupBy(edge => edge.Subject)
				.Where(edgeGroup => !inside.ContainsKey(edgeGroup.Key)
					&& !ineligibleSubjectIds.Contains(edgeGroup.Key.Id))
				.ToDictionary(edgeGroup => edgeGroup.Key, edgeGroup => edgeGroup.Select(e => e.Reference).ToList());

			return inside.Concat(near).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		public static Dictionary<ICompleteOsmGeo, List<Node>> NodesInCompleteElements(ICompleteOsmGeo[] buildings, Node[] nodes)
		{
			var byLat = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, List<Node>>();
			foreach (var building in buildings)
			{
				var bounds = building.AsBounds().ExpandBy(1); // 1 because we loose precision from double to float
				var candidates = bounds.FastNodesInBounds(byLat, byLon);
				var inners = candidates.Where(n => n.Id != building.Id && IsNodeInBuilding(n, building)).ToList();
				if (inners.Any()) result.Add(building, inners);
			}

			// If a node is in multiple buildings, remove it from all but the closest one.
			var nodesInMultipleElements = result.SelectMany(r => r.Value.Select(node => new { node, buidling = r.Key }))
				.GroupBy(pair => pair.node)
				.Where(g => g.Count() > 1)
				.ToDictionary(g => g.Key,
					g => g.Select(pair => pair.buidling)
						.OrderBy(b => DistanceMeters(g.Key, b)).ToArray());

			foreach (var inMany in nodesInMultipleElements)
			{
				foreach (var fartherBuilding in inMany.Value.Skip(1))
				{
					var inners = result[fartherBuilding];
					if (inners.Count > 1) inners.Remove(inMany.Key);
					else result.Remove(fartherBuilding);
				}
			}

			return result;
		}

		// Can return mutliple buildings having the same node near them!
		private static List<Edge> NodesNearCompleteElements(
			ICompleteOsmGeo[] subjects, Node[] references, double withinMeters)
		{
			var byLat = new SortedList<double, Node[]>(references.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(references.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, Dictionary<Node, double>>();
			var edges = new List<Edge>();

			foreach (var subject in subjects)
			{
				var bounds = subject.AsBounds().ExpandBy(withinMeters);
				var candidates = bounds.FastNodesInBounds(byLat, byLon)
					.Select(reference => new Edge() { Reference = reference, Subject = subject })
					.Where(edge => edge.Distance <= withinMeters && edge.Subject.Id != edge.Reference.Id);
				edges.AddRange(candidates);
			}

			return edges;
		}

		class Edge
		{
			public Node Reference;
			public ICompleteOsmGeo Subject;
			public double Distance {
				get {
					return distance ?? (distance = DistanceMeters(Subject, Reference)).Value;
				}
			}
			private double? distance;

			public override string ToString()
			{
				return string.Join(" ", Reference.Id, Subject.Id, distance);
			}
		}

		public static bool IsNodeInBuilding(Node node, ICompleteOsmGeo building)
		{
			if (building is Node buildingNode)
				return DistanceMeters(node, buildingNode) <= 1;
			else if (building is CompleteWay buildingWay)
			{
				var point = new NetTopologySuite.Geometries.Point(node.Longitude.Value, node.Latitude.Value);
				if (buildingWay.Nodes.Length < 4 || (buildingWay.Nodes.First() != buildingWay.Nodes.Last())) return false;
				var ring = new NetTopologySuite.Geometries.LinearRing(
					buildingWay.Nodes.Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToArray());
				var polygon = new NetTopologySuite.Geometries.Polygon(ring);
				return point.Within(polygon);
			}
			else if (building is CompleteRelation buildingRelation)
			{
				// "in" means that even if I land in the center of a donut, I'm still "in" the building.
				// This isn't 100% accurate (false negative) for polygons where the closed outer ring is defined by more than 2 open ways.
				return buildingRelation.Members.Any(m => m.Role != "inner" && IsNodeInBuilding(node, m.Member));
			}
			throw new Exception("ICompleteOsmGeo wasn't a Node, Way or Relation.");
		}

		public static void Nudge(Node[] stack, double north, double east)
		{
			int i = 1;
			foreach (var node in stack.Skip(1))
			{
				node.Longitude += i * north;
				node.Latitude += i * east;
				i++;
			}
		}

		public static Node[] AsNodes(this ICompleteOsmGeo element)
		{
			if (element is Node node) return new[] { node };
			else if (element is CompleteWay way)
			{
				return way.Nodes;
			}
			else if (element is CompleteRelation relation)
			{
				return relation.Members.SelectMany(m => AsNodes(m.Member)).ToArray();
			}
			throw new Exception("element wasn't a node, way or relation");
		}

		public static Coordinate AsPosition(this ICompleteOsmGeo element)
		{
			if (element is Node node) return new Coordinate(node.Longitude.Value, node.Latitude.Value);
			else if (element is CompleteWay way)
			{
				return new Coordinate(way.Nodes.Average(n => n.Longitude.Value), way.Nodes.Average(n => n.Latitude.Value));
			}
			else if (element is CompleteRelation relation)
			{
				// This isn't exact for relations. Good enough.
				var positions = relation.Members.Select(m => AsPosition(m.Member));

				return new Coordinate(positions.Average(n => n.X), positions.Average(n => n.Y));
			}
			throw new Exception("element wasn't a node, way or relation");
		}

		public static double MaxDistanceMeters(Coordinate from, ICompleteOsmGeo to)
		{
			return to.AsNodes().Max(n => DistanceMeters(n.AsPosition(), from));
		}

		public static double MinDistanceMeters(Coordinate from, ICompleteOsmGeo to)
		{
			return to.AsNodes().Min(n => DistanceMeters(n.AsPosition(), from));
		}

		public static double DistanceMeters(ICompleteOsmGeo left, ICompleteOsmGeo right)
		{
			return DistanceMeters(AsPosition(left), AsPosition(right));
		}

		public static double DistanceMeters(Coordinate left, Coordinate right)
		{
			const string wkt4326 = "PARAM_MT[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]]";
			var transformer = ProjNet.IO.CoordinateSystems.CoordinateSystemWktReader.Parse(wkt4326);
			//var (x1,y1) = transformer.Transform(left.X, left.Y);
			//var (x2,y2) = transformer.Transform(right.X, right.Y);
			//var c1 = new Coordinate(x1, y1);
			//var c2 = new Coordinate(x2, y2);
			//var other2 = c1.Distance(c2);

			var csWgs84 = ProjNet.CoordinateSystems.GeographicCoordinateSystem.WGS84;
			var cs27700 = ProjNet.IO.CoordinateSystems.CoordinateSystemWktReader.Parse(wkt4326); ;
			var ctFactory = new ProjNet.CoordinateSystems.Transformations.CoordinateTransformationFactory();


			var ct = ctFactory.CreateFromCoordinateSystems(csWgs84, new CoordinateSystemFactory() { }.CreateFromWkt(wkt4326));
			//var mt = ct.MathTransform;

			var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
			var a = geometryFactory.CreatePoint(left);
			var b = geometryFactory.CreatePoint(right);
			var other = a.Distance(b);

			var distance = DistanceMeters(left.Y, left.X, right.Y, right.X);
			return distance;
		}

		public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
		{
			var averageLat = (lat1 + lat2) / 2;
			var degPerRad = 180 / 3.14;
			var dLonKm = (lon1 - lon2) * 111000 * Math.Cos(averageLat / degPerRad);
			var dLatKm = (lat1 - lat2) * 110000;

			var distance = Math.Sqrt(dLatKm * dLatKm + dLonKm * dLonKm);
			return distance;
		}

		public static double DistanceLat(double distanceMeters)
		{
			// 1 lat degree = 1_000_000/9 meters
			// 1 meter = 9/1_000_000 lat degrees
			return distanceMeters * 9 / 1_000_000;
		}

		public static double DistanceLon(double distanceMeters, double latitude)
		{
			var lon = distanceMeters * 9 / 1_000_000 / Math.Cos(latitude / 180 * Math.PI);

			if (lon < 0 && distanceMeters >= 0) throw new Exception("Bad calculated lon");

			return lon;
		}

		public static bool IsOpenWay(OsmGeo subject)
		{
			return subject is Way subWay2 && subWay2.Nodes.First() != subWay2.Nodes.Last();
		}

		public static List<List<OsmGeo>> GroupCloseNeighbors(OsmGeo[] elements, double closenessMeters, IOsmGeoSource index, bool all = true)
		{
			var stacks = elements.GroupBy(a => a.AsComplete(index).AsPosition())
				.Select(stack => new
				{
					positions = new List<Coordinate> { stack.Key },
					nodes = stack.ToList()
				})
				.ToList();
			// Combine groups when everything in them is close to everything in another group.
			for (int i = 0; i < stacks.Count - 1; i++)
			{
				for (int j = i + 1; j < stacks.Count; j++)
				{
					var maybeMergeable = all ?
						stacks[i].positions.SelectMany(l => stacks[j].positions, DistanceMeters)
							.All(d => d < closenessMeters)
						: stacks[i].positions.SelectMany(l => stacks[j].positions, DistanceMeters)
							.Any(d => d < closenessMeters);
					if (maybeMergeable)
					{
						stacks[i].positions.AddRange(stacks[j].positions);
						stacks[i].nodes.AddRange(stacks[j].nodes);
						stacks.RemoveAt(j);
						j--;
					}
				}
			}

			return stacks.Select(g => g.nodes).ToList();
		}

		public static Way[] CombineSegments(Way[] ways, string idKey = null)
		{
			var highways = new HashSet<Way>(ways);

			var highwaysGrouped = highways.GroupBy(h => new TagsCollection(h.Tags.Where(t => idKey != t.Key)));
			var doneNames = new HashSet<TagsCollection>();

			restart: // gross but effective
			var highwaysByNames = highwaysGrouped.Where(g => g.Count() > 1 && !doneNames.Contains(g.Key));

			foreach (var byName in highwaysByNames)
			{
				var ends = byName.SelectMany(highway => new[] { highway.Nodes.First(), highway.Nodes.Last() }.Select(node => new { node, highway }))
					.GroupBy(nh => nh.node).Select(g => g.Select(gw => gw.highway).Distinct());

				var intersection = ends.FirstOrDefault(g => g.Count() > 1)?.ToArray();
				if (intersection != null)
				{
					CombineSegments(intersection[0], intersection[1]);
					highways.Remove(intersection[1]);
					if (idKey != null) intersection[0].Tags.AddOrAppend(idKey, intersection[1].Tags[idKey]);
					goto restart;
				}
				else
				{
					doneNames.Add(byName.Key);
				}
			}

			return highways.ToArray();
		}

		public static void CombineSegments(Way subject, Way reference)
		{
			if (!subject.Tags.Equals(reference.Tags)) throw new Exception("these tags are different, can't merge ways");

			if (subject.Nodes.Last() == reference.Nodes.First())
			{
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.Last() == reference.Nodes.Last())
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new Exception("Reversing a oneway");
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Reverse().Skip(1)).ToArray();
			}
			else if (subject.Nodes.First() == reference.Nodes.Last())
			{
				subject.Nodes = reference.Nodes.Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.First() == reference.Nodes.First())
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new Exception("Reversing a oneway");
				subject.Nodes = reference.Nodes.Reverse().Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else
			{
				throw new Exception("Ways are not end-to-end, and can't be combined");
			}
		}

		public static CompleteWay[] Disolve(CompleteWay[] ways, string idKey)
		{
			var highwaysGrouped = ways.GroupBy(h => new TagsCollection(h.Tags.Where(t => t.Key != idKey)), new TagsCollectionComparer()).Select(g => g.ToList()).ToArray();
			var deadNodes = new HashSet<long>();

			foreach (var byName in highwaysGrouped)
			{
				bool progress = true;
				while (progress)
				{
					progress = false;
					var intersections = byName.SelectMany(highway => new[] {
							new { node = highway.Nodes.First(), highway },
							new { node = highway.Nodes.Last(), highway }
						})
						.GroupBy(nh => nh.node)
						.Where(g => !deadNodes.Contains(g.Key.Id.Value))
						.Select(g => g.Select(gw => gw.highway).Distinct());

					var intersection = intersections.FirstOrDefault(g => g.Count() > 1)?.ToArray();

					if (intersection == null)
					{
						break;
					}

					for (int i = 0; i < intersection.Length - 1 && !progress; i++)
						for (int j = i + 1; j < intersection.Length && !progress; j++)
						{
							try
							{
								CombineSegments(intersection[i], intersection[j]);
								intersection[i].Tags.AddOrAppend(idKey, intersection[j].Tags[idKey]);
								byName.Remove(intersection[j]);
								progress = true;
							}
							catch { }
						}

					if (!progress)
					{
						var commonNodeId = intersection.Select(w => w.Nodes.Select(n => n.Id.Value)).Aggregate((a, b) => a.Intersect(b)).First();
						deadNodes.Add(commonNodeId);
						progress = true;
					}
				}
			}

			return highwaysGrouped.SelectMany().ToArray();
		}

		public static void CombineSegments(CompleteWay subject, CompleteWay reference)
		{
			if (subject.Nodes.Last().Id == reference.Nodes.First().Id)
			{
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.Last().Id == reference.Nodes.Last().Id)
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new InvalidOperationException("Reversing a oneway");
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Reverse().Skip(1)).ToArray();
			}
			else if (subject.Nodes.First().Id == reference.Nodes.Last().Id)
			{
				subject.Nodes = reference.Nodes.Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.First().Id == reference.Nodes.First().Id)
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new InvalidOperationException("Reversing a oneway");
				subject.Nodes = reference.Nodes.Reverse().Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else
			{
				throw new Exception("Ways are not end-to-end, and can't be combined");
			}
		}
	}
}
