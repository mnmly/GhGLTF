VERSION := $(shell xmllint --xpath "//Project/PropertyGroup/Version/text()" GhGLTF/GhGLTF.csproj)
DIR = GhGLTF/bin/Release/netstandard2.0

manifest:
	sed -i -- 's/[[:digit:]]\.[[:digit:]]\.[[:digit:]]/$(VERSION)/g' $(DIR)/manifest.yml
	rm $(DIR)/manifest.yml--

build: manifest
	cd $(DIR) && /Applications/RhinoWIP.app/Contents/Resources/bin/yak build

publish:
	/Applications/RhinoWIP.app/Contents/Resources/bin/yak push $(target)

install:
	/Applications/RhinoWIP.app/Contents/Resources/bin/yak install GhGLTF
	/Applications/Rhinoceros.app/Contents/Resources/bin/yak install GhGLTF
