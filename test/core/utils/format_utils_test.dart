import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/utils/count_format.dart';
import 'package:karar/core/utils/tr_case.dart';

void main() {
  group('formatCompactCount', () {
    test('plain below 1 000', () {
      expect(formatCompactCount(0), '0');
      expect(formatCompactCount(456), '456');
      expect(formatCompactCount(999), '999');
    });

    test('Turkish thousands dot below 10 000', () {
      expect(formatCompactCount(1000), '1.000');
      expect(formatCompactCount(1247), '1.247');
      expect(formatCompactCount(9999), '9.999');
    });

    test('bin with comma decimal, truncated', () {
      expect(formatCompactCount(10000), '10B');
      expect(formatCompactCount(12400), '12,4B');
      expect(formatCompactCount(12499), '12,4B');
      expect(formatCompactCount(999999), '999,9B');
    });

    test('milyon', () {
      expect(formatCompactCount(1000000), '1Mn');
      expect(formatCompactCount(1250000), '1,2Mn');
    });
  });

  group('Turkish case', () {
    test('trUpper maps dotted and dotless i', () {
      expect(trUpper('i'), 'İ');
      expect(trUpper('ı'), 'I');
      expect(trUpper('bildirimler'), 'BİLDİRİMLER');
    });

    test('trLower maps İ and I', () {
      expect(trLower('İ'), 'i');
      expect(trLower('I'), 'ı');
      expect(trLower('İŞ HAYATI'), 'iş hayatı');
    });

    test('trFold folds diacritics for lookups', () {
      expect(trFold('İş Hayatı'), 'is hayati');
      expect(trFold('Arkadaşlık'), 'arkadaslik');
      expect(trFold(' Diğer '), 'diger');
    });
  });
}
