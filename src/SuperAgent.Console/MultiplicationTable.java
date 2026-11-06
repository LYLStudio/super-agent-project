public class MultiplicationTable {
    public static void main(String[] args) {
        System.out.println("99乘法表");
        for (int i = 1; i <= 9; i++) {
            for (int j = 1; j <= i; j++) {
                System.out.printf("%d x %d = %2d  ", i, j, i * j);
            }
            System.out.println();
        }
    }
}
