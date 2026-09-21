// A stand-in for the server's image route, for the specs that check where the pages load their images from.
// The harness pages load from file:, where a relative address cannot be routed, so the mock ApiClient is made to
// answer the image route with an https address on a pretend server that a spec can intercept.
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

const SERVER = 'https://server.test';

// Makes ApiClient.getUrl send the image route to SERVER. Run it before the page renders anything.
async function pointImagesAtTheServer(page) {
    await page.evaluate((server) => {
        const original = window.ApiClient.getUrl;
        window.ApiClient.getUrl = (path, query) => path === 'MindTheGaps/Image'
            ? server + '/MindTheGaps/Image?u=' + encodeURIComponent(query.u)
            : original(path, query);
    }, SERVER);
}

// Answers the pretend server's image route and the provider hosts. serverStatus is what the route says (200 serves
// the image, anything else stands for a cache that could not serve it); providerStatus is what a provider says
// when the page goes to it directly. Returns the addresses requested, in order, per side.
async function serveImages(page, serverStatus, providerStatus) {
    const seen = { server: [], provider: [] };
    await page.route('https://server.test/**', (route) => {
        seen.server.push(route.request().url());
        return serverStatus === 200
            ? route.fulfill({ status: 200, contentType: 'image/png', body: PNG })
            : route.fulfill({ status: serverStatus, body: '' });
    });
    await page.route(/^https:\/\/(image\.tmdb\.org|example\.test)\//, (route) => {
        seen.provider.push(route.request().url());
        return providerStatus === 200
            ? route.fulfill({ status: 200, contentType: 'image/png', body: PNG })
            : route.fulfill({ status: providerStatus, body: '' });
    });
    return seen;
}

module.exports = { SERVER, pointImagesAtTheServer, serveImages };
