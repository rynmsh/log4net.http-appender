$PACKAGE_VERSION = "0.0.3"

# Build solution
$slnPath = "..\Log4Net.HttpAppender.NET45.sln"

# Build the solution
msbuild $slnPath /p:Configuration=Release

# initial setup and init
$tempDirectory = ".\temp"
$srcDirectory = "..\Log4Net.HttpAppender"

# Remove temp directory if it exists
if (test-path($tempDirectory)) {
  remove-item $tempDirectory -Force -Recurse
}

# create new temp directory	
new-item "$tempDirectory\lib\net40" -type directory

# copy nuspec
copy-item "$srcDirectory\package.nuspec" "$tempDirectory\package.nuspec"

# copy the assemblies to the correct paths
copy-item "$srcDirectory\bin\Release\Log4Net.HttpAppender.dll" "$tempDirectory\lib\net40\Log4Net.HttpAppender.dll"

$packedFile = "Log4Net.HttpAppender.$PACKAGE_VERSION.nupkg"

try {
  # create the package file
  nuget pack "$tempDirectory\package.nuspec" -version $PACKAGE_VERSION

  # now upload package
  nuget push $packedFile
}
finally {
  # remove local package
  if (test-path($packedFile)) {
	remove-item $packedFile -Force
  }

  # cleanup
  remove-item $tempDirectory -Recurse -Force
}