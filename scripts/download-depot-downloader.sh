#!/bin/bash

if [ "$(arch)" == "aarch64" ]; then
  wget https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.7.4/DepotDownloader-linux-arm64.zip -O ./DepotDownloader.zip
else
  wget https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.7.4/DepotDownloader-linux-x64.zip -O ./DepotDownloader.zip
fi

unzip ./DepotDownloader.zip
chmod +x ./DepotDownloader
rm ./DepotDownloader.*
