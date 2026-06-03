import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('SearchScreen follows documented 400ms debounce', () {
    final text =
        File('lib/features/search/search_screen.dart').readAsStringSync();

    expect(text, contains('Duration(milliseconds: 400)'));
  });

  test('user search does not add a second debounce after screen debounce', () {
    final text = File('lib/features/search/user_search_provider.dart')
        .readAsStringSync();

    expect(text, isNot(contains('Timer(')));
    expect(text, contains('_fetch(query)'));
  });
}
