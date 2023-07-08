window.getCurrentLocation = () => {
    return new Promise((resolve, reject) => {
        if (!navigator.geolocation) {
            reject('Geolocation is not supported by your browser');
        } else {
            navigator.geolocation.getCurrentPosition((position) => {
                resolve({ lat: position.coords.latitude, lng: position.coords.longitude });
            }, () => reject('Unable to retrieve your location'));
        }
    });
}