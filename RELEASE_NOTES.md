#### 0.2.13 - 12/09/2016
* FIXED: publisher produces invalid manifest after publishing secondary pkgs with the same main pkg twice

#### 0.2.12 - 12/02/2016
* FIXED: updater forwards to older version due to incorrect code for reading assembly version

#### 0.2.11 - 12/02/2016
* FIXED: duplicate pkgs fail over HTTP, use "-d%d-" for duplicate name suffix instead
* publisher to skip packages that already exist

#### 0.2.10 - 11/23/2016
* minor change in logging

#### 0.2.9 - 10/29/2016
* FIXED: updater only update still promts for new version confirmation

#### 0.2.8 - 10/29/2016
* FIXED: does not reuse parent pkg on subsequent updates of secondary pkgs

#### 0.2.7 - 10/29/2016
* FIXED: fails on update when a pkg from the current manifest is missing

#### 0.2.6 - 10/27/2016
* FIXED: fails update the updater and the app pkgs in one update
* FIXED: updater forwards to previous version

#### 0.2.5 - 10/26/2016
* wait for another instance of the updater if it's already running

#### 0.2.4 - 10/25/2016
* trim .manifest.json from the end of version argument

#### 0.2.3 - 10/20/2016
* FIXED: update only updates prompt user confirmation
* add *version* parameter to launch a particular version of the app (opposed to the latest)
* zip logs

#### 0.2.2 - 10/20/2016
* skip cleanup of updater packages (enabled by default)

#### 0.2.1 - 10/19/2016
* logging

#### 0.2.0 - 10/17/2016
* support update of only secondary packages 
* forward to the latest version of the updater
* restart the updater if a new version of the updater is downloaded
* cleanup of old and temp artifacts

#### 0.1.4 - 09/02/2016
* publish tools in nupkgs

#### 0.1.3 - 09/01/2016
* FIXED: app.version is not updated

#### 0.1.2 - 09/01/2016
* FIXED: package name with dash symbols
* FIXED: replaces variable expressions

#### 0.1.1 - 09/01/2016
* Setup project
* basic updater
* publisher publish command
