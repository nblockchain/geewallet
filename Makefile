all:
	@./build/make.sh

install:
	@./build/make.sh install

check:
	@./build/make.sh check

release:
	@./build/make.sh release

zip:
	@./build/make.sh zip

run:
	@./build/make.sh run

update-servers:
	@./build/make.sh update-servers

nuget:
	@./build/make.sh nuget
