import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('robots.txt allows public pages and points to sitemap', () {
    final robots = File('web/robots.txt').readAsStringSync();

    expect(robots, contains('User-agent: *'));
    expect(robots, contains('Allow: /'));
    expect(robots, contains('Disallow: /api/'));
    expect(robots, contains('Sitemap: https://karar.app/sitemap.xml'));
  });

  test('static sitemap lists indexable public and legal routes', () {
    final sitemap = File('web/sitemap.xml').readAsStringSync();

    for (final route in [
      '/',
      '/discover',
      '/legal/terms',
      '/legal/privacy',
      '/legal/community',
      '/legal/content-policy',
      '/legal/contact',
    ]) {
      expect(sitemap, contains('<loc>https://karar.app$route</loc>'));
    }
  });

  test('web index contains base Open Graph and Twitter preview tags', () {
    final index = File('web/index.html').readAsStringSync();

    for (final tag in [
      '<meta name="robots" content="index, follow">',
      '<meta property="og:type" content="website">',
      '<meta property="og:site_name" content="Karar">',
      '<meta property="og:title"',
      '<meta property="og:description"',
      '<meta property="og:image" content="https://karar.app/og-image.png">',
      '<meta property="og:image:width" content="1200">',
      '<meta property="og:image:height" content="630">',
      '<meta name="twitter:card" content="summary_large_image">',
      '<meta name="twitter:image" content="https://karar.app/og-image.png">',
    ]) {
      expect(index, contains(tag));
    }

    expect(File('web/og-image.png').existsSync(), isTrue);
  });
}
