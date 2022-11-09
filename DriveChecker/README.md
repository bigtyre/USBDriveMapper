This program is designed to maintain a volume mapping to a target directory, attempting to reconnect it whenever it is disconnected. This is aimed at using rotated hard drives to back a directory used for backups. 

Configure the app using appSettings.json. You need to set the target directory and check interval, as well as add volume names as potential attachment targets.

To add a hard drive as an attachment target run the command `mountvol` from a Windows command prompt to see a list of volume names. Copy the volume name to the VolumeNames list in appSettings.json to add the hard drive to the pool, ensuring any slashes are quoted as necessary for a JSON file.

This program uses the Windows mountvol command to manage volume mappings and so must be run as administrator.