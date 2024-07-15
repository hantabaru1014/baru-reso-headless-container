#!/bin/bash

wget https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.6.0/DepotDownloader-linux-x64.zip -O ./DepotDownloader.zip

unzip ./DepotDownloader.zip
chmod +x ./DepotDownloader
rm ./DepotDownloader.*
