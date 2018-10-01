﻿function createApiService($http) {
    var srv = this;

    srv.apiStatus =
        {
            isReachable: false,
            errorMessage: null
        };

    srv.apiStatusUpdatedCallback = null;
    srv.newStatusReceivedCallback = null;

    srv.waitForStatus = function () {
        var parameters = {
            method: "POST",
            url: "/api/v1/message_bus/wait_for?timeout=5",
            data: [{ type: "component_registry.event.status_changed" }]
        }

        $http(parameters).then(function (response) {
            srv.pollStatus();
        }, function () {
            srv.pollStatus();
        })
    }

    srv.pollStatus = function () {
        var successHandler = function (status) {
            srv.apiStatus.isReachable = true;

            if (srv.newStatusReceivedCallback != null) {
                srv.newStatusReceivedCallback(status);
            }

            srv.waitForStatus();
        };

        var errorHandler = function () {
            // Always poll the status on errors. Otherwise the app
            // will wait for the next change and ignores changes
            // which happened already (while being offline)
            srv.apiStatus.isReachable = false;
            setTimeout(function () { srv.pollStatus(); }, 5000);
        };

        var status = {};
        var promises = [];

        promises.push(new Promise(function (resolve, reject) {
            $http.get("/api/v1/areas").then(
                function (response) {
                    status.areas = response.data;
                    resolve();
                },
                function () {
                    reject();
                });
        }));

        promises.push(new Promise(function (resolve, reject) {
            $http.get("/api/v1/components").then(
                function (response) {
                    status.components = response.data;
                    resolve();
                },
                function () {
                    reject();
                });
        }));

        promises.push(new Promise(function (resolve, reject) {
            $http.get("/api/v1/global_variables").then(
                function (response) {
                    status.global_variables = response.data;
                    resolve();
                },
                function () {
                    reject();
                });
        }));

        promises.push(new Promise(function (resolve, reject) {
            $http.get("/api/v1/notifications").then(
                function (response) {
                    status.notifications = response.data;
                    resolve();
                },
                function () {
                    reject();
                });
        }));

        Promise.all(promises).then(
            function () { successHandler(status); },
            function () { errorHandler(); });
    }

    srv.executePost = function (uri, data, successCallback) {
        $http.post(uri, data).then(function (response) {
            if (successCallback != undefined) {
                successCallback(response.data);
            }
        });
    }

    srv.executeDelete = function (uri, data, successCallback) {
        $http.delete(uri, data).then(function (response) {
            if (successCallback != undefined) {
                successCallback(response.data);
            }
        });
    }

    srv.deleteNotification = function (uid) {
        $http.delete("/api/v1/notifications/" + uid);
    }

    return this;
}