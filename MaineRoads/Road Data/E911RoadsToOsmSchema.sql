-- Data Corrections
Update elements set route_num = '' where route_num = '0';
Update elements set SPEED = '' where speed in ('0', '2', '3', '253');
Update elements set route_num = '202/4' where objectid = '145597' AND route_num = '2002/4';
update elements set STREETNAME = 'Green' where objectid in ('25384', '131257') AND STREETNAME = 'Greene';

-- Schema Translation
SELECT
		Elements.xid, -- Required first column
		Elements.xtype, -- Required second column

		COALESCE(pre.value || ' ', '')
			|| STREETNAME
			|| COALESCE(' ' || suf.value, '')
			|| COALESCE(' ' || post.value, '') as [name],

		CASE ONEWAY
			WHEN 'FT' THEN 'yes'
			WHEN 'TF' THEN '-1' -- way could be reversed, oneway changed to yes
		END as [oneway],

		-- split on /, add ME_ (or US_ for 1, 1A, 2, 2A, 201, 201A, 202, 302), join with ;
		CASE ROUTE_NUM
			WHEN '' THEN null
			ELSE
			== https://www.samuelbosch.com/2018/02/split-into-rows-sqlite.html
				RTRIM(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
						'ME ' || replace(ROUTE_NUM, '/', ';ME ') || ';',
					-- https://en.wikipedia.org/wiki/List_of_U.S._Highways_in_Maine
					'ME 1;', 'US 1;'),
					'ME 1A;', 'US 1A;'),
					'ME 2;', 'US 2;'),
					'ME 2A;', 'US 2A;'),
					'ME 201;', 'US 201;'),
					'ME 201A;', 'US 201A;'),
					'ME 202;', 'US 202;'),
					'ME 302;', 'US 302;'),
					-- https://en.wikipedia.org/wiki/List_of_Interstate_Highways_in_Maine
					'ME 95;', 'I 95;'),
					'ME 195;', 'I 195;'),
					'ME 295;', 'I 295;'),
					'ME 395;', 'I 395;'),
					'ME 495;', 'I 495;')
				, ';')
		END as [ref],

		otherTags.*, -- highway:residential could be {primary, seconday, tertiary, residential}

		OBJECTID as [medot:objectid]

		-- Get ref as alt name (https://en.wikipedia.org/wiki/Module:Road_data/strings/USA/ME)
		-- Punctualize from local sources?
		-- find matching names from other subject fields
	FROM Elements
	LEFT JOIN Directions as pre
		ON pre.id = PREDIR
	LEFT JOIN Directions as post
		ON post.id = POSTDIR
	LEFT JOIN StreetSuffixes as suf
		ON suf.id = SUFFIX
	LEFT JOIN RoadClasses as otherTags
		ON otherTags.id = RDCLASS
	WHERE highway != 'proposed' AND xtype = 'Way'