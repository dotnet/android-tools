CONFIGURATION   := Debug
OS              := $(shell uname)
V               ?= 0

include build-tools/scripts/msbuild.mk

all:
	$(MSBUILD) /restore $(MSBUILD_FLAGS) Xamarin.Android.Tools.sln

clean:
	-$(MSBUILD) $(MSBUILD_FLAGS) /t:Clean Xamarin.Android.Tools.sln

prepare:
	nuget restore Xamarin.Android.Tools.sln

run-all-tests: run-nunit-tests

# $(call RUN_NUNIT_TEST,filename,log-lref?)
define RUN_NUNIT_TEST
	dotnet test $(1) -l "console;verbosity=detailed"
endef

NUNIT_TESTS = \
	bin/Test$(CONFIGURATION)/netcoreapp3.1/Xamarin.Android.Tools.AndroidSdk-Tests.dll

run-nunit-tests: $(NUNIT_TESTS)
	$(foreach t,$(NUNIT_TESTS), $(call RUN_NUNIT_TEST,$(t),1))
