#!/usr/bin/env fish

function csproj_field
	set csproj $argv[1]
	set field $argv[2]
	set value (string replace -rf "\\s*<$field>(.*)</$field>" '$1' < $csproj | string trim)[1]

	if ! string match -qr -- '.' $value
		echo "Could not extract value of $field from $csproj" 1>&2
		exit 1
	end

	echo $value
end

function publish_csproj
	set csproj $argv[1]
	if ! test -f $csproj
		echo "Could not find project file $csproj!" 1>&2
		exit 1
	end

	set -l pkgname (csproj_field $csproj "PackageId")
	set -l pkgversion (csproj_field $csproj "Version")

	if ! dotnet build -c Release -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg $csproj
		exit 1
	end

	set nupkg (dirname $csproj)/bin/Release/$pkgname.$pkgversion.nupkg
	if ! test -f $nupkg
		echo "Could not find nuget package $nupkg!" 1>&2
		exit 1
	end

	set snupkg (dirname $csproj)/bin/Release/$pkgname.$pkgversion.snupkg
	if ! test -f $nupkg
		echo "Could not find nuget symbol package $snupkg!" 1>&2
		exit 1
	end

	if ! nuget push $nupkg
		exit 1
	end

	if ! nuget push $snupkg
		exit 1
	end

end

if string match -qr -- . $argv[1]
	set csproj $argv[1]
	publish_csproj $csproj
else
	publish_csproj ./AsyncLock/*.csproj
end
