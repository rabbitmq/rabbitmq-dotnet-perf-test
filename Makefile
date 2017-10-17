MKDIR_P := mkdir -p

DPT := ./src/dpt/dpt.fsproj

VERSION := 0.1.0

.PHONY: restore build all clean

all: restore build

restore:
	dotnet restore $(DPT)

build:
	dotnet build $(DPT)

clean:
	dotnet clean $(DPT)
	rm -rf ./releases

prep-releases:
	rm -rf ./releases
	mkdir ./releases

rids := osx.10.10-x64 win7-x64 debian.8-x64

$(rids): prep-releases
	dotnet publish $(DPT) --self-contained -c Release -r $@ -o ../../tmp/$@
	zip ./releases/$@_$(VERSION).zip ./tmp/$@
	rm -rf ./tmp

release: $(rids)
