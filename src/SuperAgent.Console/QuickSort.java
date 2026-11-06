public class QuickSort {
    // 主方法，用於測試
    public static void main(String[] args) {
        int[] arr = {10, 7, 8, 9, 1, 5};
        System.out.println("排序前: ");
        printArray(arr);

        quickSort(arr, 0, arr.length - 1);

        System.out.println("排序後: ");
        printArray(arr);
    }

    // 快速排序主函式
    public static void quickSort(int[] arr, int low, int high) {
        if (low < high) {
            // 取得分割點
            int pi = partition(arr, low, high);

            // 遞迴排序分割點左側與右側
            quickSort(arr, low, pi - 1);
            quickSort(arr, pi + 1, high);
        }
    }

    // 分割函式
    private static int partition(int[] arr, int low, int high) {
        int pivot = arr[high]; // 選擇最後一個元素作為 pivot
        int i = low - 1; // 指向小於 pivot 的元素

        for (int j = low; j < high; j++) {
            if (arr[j] <= pivot) {
                i++;
                swap(arr, i, j);
            }
        }
        swap(arr, i + 1, high);
        return i + 1;
    }

    // 交換元素
    private static void swap(int[] arr, int i, int j) {
        int temp = arr[i];
        arr[i] = arr[j];
        arr[j] = temp;
    }

    // 印出陣列
    private static void printArray(int[] arr) {
        for (int value : arr) {
            System.out.print(value + " ");
        }
        System.out.println();
    }
}
