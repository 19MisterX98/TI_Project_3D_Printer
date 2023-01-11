import octoprint.plugin
import flask
import os
import octoprint.filemanager.util


class TiProjektPlugin(octoprint.plugin.SimpleApiPlugin,
                       octoprint.plugin.SettingsPlugin):

    def __init__(self):
        super().__init__()

    def on_api_get(self, request):

        if not self._printer.get_state_id() == "PRINTING":
            return flask.jsonify(Error="Not printing")

        job_data = self._printer.get_current_job()

        file_data = job_data["file"]
        file_path = file_data["path"]

        file_path = self._settings.getBaseFolder("base") + "/uploads/" + file_path

        base_name, _ = os.path.splitext(file_path)
        new_file_path = base_name + ".obj"

        info = False

        with open(new_file_path, "w") as output_file:
            with open(file_path, "r") as input_file:
                for line in input_file:
                    if line.startswith(";obj"):
                        line = line.replace(";obj", "")
                        output_file.write(line)
                        info = True

        print(new_file_path)
        if info:
            return flask.send_file(new_file_path)
        else:
            return flask.jsonify(Error="No obj available")


__plugin_name__ = "ti_projekt"
__plugin_version__ = "1.0.0"
__plugin_description__ = "Extracts obj from modified gcode via http get"
__plugin_pythoncompat__ = ">=3.4,<4"
__plugin_implementation__ = TiProjektPlugin()

