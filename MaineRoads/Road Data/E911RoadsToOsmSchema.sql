-- References
-- https://www.samuelbosch.com/2018/02/split-into-rows-sqlite.html
-- https://en.wikipedia.org/wiki/List_of_Interstate_Highways_in_Maine

-- Data Corrections
Update elements set route_num = '' where route_num = '0';
Update elements set SPEED = '' where speed in ('0', '2', '3', '253');
Update elements set route_num = '202/4' where objectid = '145597' AND route_num = '2002/4';
update elements set STREETNAME = 'Green' where objectid in ('25384', '131257') AND STREETNAME = 'Greene';
update elements set ONEWAY = 'FT' where objectid in ('104293') and ONEWAY is null;
update elements set streetname = 'William L Clarke Drive' where objectid in ('6225','22450','49879','76123','6693','67108','103602','19485','59216','138476','95558','113127') and streetname = 'William B Clarke Drive';

-- Schema Translation
WITH routes(xid, xtype, [ref], [alt_name]) AS (
	-- Split RouteNum by /
	WITH RECURSIVE route_nums(xid, xtype, route, rest) AS (
		SELECT xid, xtype, '', route_num || '/' FROM elements WHERE route_num
			UNION ALL SELECT xid, xtype,
				Substr(rest, 0, Instr(rest, '/')),
				Substr(rest, Instr(rest, '/') + 1)
			FROM route_nums
			WHERE rest <> '')
	-- Translate RouteNum into ref, and rejoin with ;
	SELECT
			xid,
			xtype,
			group_concat(
				Coalesce(
					prefix.value,
					(select value from RoutePrefixes where id = '*'))
				|| ' ' || [route], ';') AS [ref]
		FROM route_nums
		LEFT JOIN RoutePrefixes AS prefix
			ON route = prefix.id
		WHERE route <> ''
		ORDER BY [route]
		GROUP by xid, xtype)
SELECT
		Elements.xid, -- Required first column
		Elements.xtype, -- Required second column
		COALESCE(pre.value || ' ', '')
			|| STREETNAME
			|| COALESCE(' ' || suf.value, '')
			|| COALESCE(' ' || post.value, '') AS [name],
		CASE ONEWAY
			WHEN 'FT' THEN 'yes'
			WHEN 'TF' THEN '-1' -- Later: reverse these ways, change to 'yes'
		END AS [oneway],
		Routes.ref,
		otherTags.*, -- highway:residential could be {primary, seconday, tertiary, residential, unclassified}
		OBJECTID AS [medot:objectid]
	FROM Elements
	LEFT JOIN Routes
		ON Routes.xid = Elements.xid
			AND Routes.xtype = Elements.xtype
	LEFT JOIN Directions AS pre
		ON pre.id = PREDIR
	LEFT JOIN Directions AS post
		ON post.id = POSTDIR
	LEFT JOIN StreetSuffixes AS suf
		ON suf.id = SUFFIX
	LEFT JOIN RoadClasses AS otherTags
		ON otherTags.id = RDCLASS
	WHERE highway != 'proposed'
		AND Elements.xtype = 'Way'